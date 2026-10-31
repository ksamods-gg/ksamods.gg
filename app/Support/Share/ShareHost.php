<?php

namespace App\Support\Share;

use App\Jobs\MirrorShareRelease;
use App\Jobs\ProposeShareListing;
use App\Models\Share;
use App\Models\ShareRelease;
use App\Models\ShareUpload;
use App\Models\User;
use App\Support\ContentIndex;
use App\Support\Toml;
use Devium\Toml\Toml as TomlParser;
use Illuminate\Http\UploadedFile;
use Illuminate\Support\Str;
use Throwable;

/**
 * The control plane of ksamods.gg as a share host (RFC 0094, spec/share-host.md): checks an
 * upload, then publishes it in the order of rule 9, so no reader sees a reference to something
 * that does not exist yet: the archive, the owner record of a new id, the release list entry,
 * the attestation, the pull request. The API and the website both come through here.
 */
class ShareHost
{
    // RFC 0031: the id of a listing, and the version forms of the authored schema.
    public const ID = '/^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$/';

    public const SEMVER = '/^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*)?(?:\+[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*)?$/';

    public const BOUND = '/^(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){0,2}(?:-(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*)?(?:\+[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*)?$/';

    public const STATUSES = ['stable', 'testing', 'dev'];

    public const KINDS = ['required', 'optional', 'recommends', 'suggests', 'conflict'];

    /**
     * Checks an archive and keeps it for publishing, or refuses it with the checks that failed.
     *
     * @param  string  $dependencies  RFC 0085's list as TOML, an array of [[dependencies]] tables.
     * @param  string|null  $listingId  For a new version of a listing the user owns.
     *
     * @throws ShareError
     */
    public static function upload(User $user, UploadedFile $archive, string $dependencies, ?string $listingId, ?string $client = null): ShareUpload
    {
        $share = null;

        if ($listingId !== null && $listingId !== '') {
            $share = Share::where('listing_id', $listingId)->first()
                ?? throw new ShareError('not_owner', "There is no listing {$listingId} on this host.", 404);

            if ($share->user_id !== $user->id) {
                throw new ShareError('not_owner', "Your account does not own {$listingId}.", 403);
            }
        }

        $entries = self::dependencies($dependencies);
        $path = (string) $archive->getRealPath();
        $size = (int) $archive->getSize();
        $inspection = ShareArchive::inspect($path, $size, $share?->type);

        if ($inspection->passed()) {
            self::checkFolder($inspection, $share);
        }

        if (! $inspection->passed()) {
            throw new ShareError((string) $inspection->error, self::failure($inspection), match ($inspection->error) {
                'too_large' => 413,
                'type_not_accepted' => 415,
                default => 422,
            }, ['checks' => $inspection->checks]);
        }

        $upload = new ShareUpload([
            'share_id' => $share?->id,
            'type' => $inspection->type,
            'size' => $size,
            'sha256' => hash_file('sha256', $path),
            'folder' => $inspection->folder,
            'game_version' => $inspection->gameVersion,
            'dependencies' => $entries,
            'checks' => $inspection->checks,
            'passed' => true,
            'client' => $client,
            'expires_at' => now()->addHours((int) config('share.upload_hours')),
        ]);
        $upload->id = (string) Str::ulid();
        $upload->path = "uploads/{$upload->id}.zip";
        DataFiles::disk()->putFileAs('uploads', $archive, "{$upload->id}.zip", ['visibility' => 'private']);
        $upload->user()->associate($user)->save();

        return $upload;
    }

    /**
     * The answer to an upload (rule 15): its id, the checks, a proposed version, the listing text
     * to show the player, and when it expires.
     *
     * @return array<string, mixed>
     */
    public static function answer(ShareUpload $upload): array
    {
        return [
            'upload' => $upload->id,
            'type' => $upload->type,
            'checks' => $upload->checks,
            'version' => self::proposedVersion($upload->share),
            'listing' => self::listingText($upload),
            'expires' => $upload->expires_at->utc()->format('Y-m-d\TH:i:s\Z'),
        ];
    }

    /**
     * Publishes a checked upload as a release, and for a new id as a new listing.
     *
     * @param  array<string, mixed>  $input  `version`, `status`, and for a new id `id`, `name`, `authors`, `abstract`, `license`, `description`, `tags`.
     * @return array{id: string, type: string, version: string, pull_request: string|null}
     *
     * @throws ShareError
     */
    public static function publish(User $user, ShareUpload $upload, array $input): array
    {
        if ($upload->user_id !== $user->id) {
            throw new ShareError('not_owner', 'This upload belongs to another account.', 403);
        }

        if ($upload->published_at !== null) {
            throw new ShareError('upload_expired', 'This upload is already published. Upload the archive again for another release.', 410);
        }

        if ($upload->expired()) {
            throw new ShareError('upload_expired', 'This upload expired. Upload the archive again.', 410);
        }

        $version = (string) ($input['version'] ?? '');
        $status = (string) ($input['status'] ?? 'stable');

        if (preg_match(self::SEMVER, $version) !== 1) {
            throw new ShareError('invalid_request', 'The version must be a SemVer version with three numbers, such as 1.0.0.');
        }

        if (! in_array($status, self::STATUSES, true)) {
            throw new ShareError('invalid_request', 'The status must be stable, testing or dev.');
        }

        $share = $upload->share;
        $new = $share === null;

        if ($share !== null && $share->user_id !== $user->id) {
            throw new ShareError('not_owner', "Your account does not own {$share->listing_id}.", 403);
        }

        // A version is never given out twice, also when its release was taken down (rule 11).
        if ($share !== null && $share->releases()->where('version', $version)->exists()) {
            throw new ShareError('version_taken', "{$share->listing_id} already had version {$version}. Choose a new version.", 409);
        }

        $share ??= self::newListing($user, (string) $upload->type, $input);

        // 1. The archive, downloadable at its url.
        $path = "archives/{$share->type}/{$share->listing_id}/{$version}/{$share->listing_id}-{$version}.zip";
        $disk = DataFiles::disk();
        $disk->writeStream($path, $disk->readStream($upload->path) ?? throw new ShareError('upload_expired', 'The uploaded archive is gone. Upload it again.', 410), [
            'visibility' => 'public',
            'ContentType' => 'application/zip',
        ]);
        $release = new ShareRelease([
            'version' => $version,
            'status' => $status,
            'path' => $path,
            'size' => $upload->size,
            'sha256' => $upload->sha256,
            'folder' => $upload->folder,
            'game_version' => $upload->game_version,
            'dependencies' => $upload->dependencies,
        ]);
        $share->releases()->save($release);

        // 2. The owner record of a new id.
        if ($new) {
            DataFiles::writeOwnerRecord($share);
        }

        $upload->forceFill(['published_at' => now(), 'share_id' => $share->id])->save();
        $disk->delete($upload->path);

        // A new listing, or a new dependency list, goes to the index by pull request.
        $propose = $new || $share->dependencies !== $upload->dependencies;

        if ($propose) {
            $share->forceFill(['dependencies' => $upload->dependencies])->save();
        }

        // 3 to 5. The release list entry, then the attestation and the pull request.
        $release->forceFill(['listed_at' => now()])->save();
        DataFiles::writeReleaseList($share);
        $pullRequest = $propose ? self::propose($share, $new) : null;

        // The Internet Archive copy follows from the queue, and joins the entry once it is served.
        if (ArchiveMirror::enabled()) {
            MirrorShareRelease::dispatch($release);
        }

        return ['id' => $share->listing_id, 'type' => $share->type, 'version' => $version, 'pull_request' => $pullRequest];
    }

    /**
     * Attests the listing's document and opens or updates its pull request. When GitHub cannot be
     * reached, a queued job tries again, and the release is published regardless.
     */
    public static function propose(Share $share, bool $new): ?string
    {
        try {
            return ProposeShareListing::run($share, $new);
        } catch (Throwable $error) {
            report($error);
            ProposeShareListing::dispatch($share, $new)->delay(now()->addMinute());

            return null;
        }
    }

    /**
     * 1.0.0 for a first release, and after that the next minor version (RFC 0094, Guide).
     */
    public static function proposedVersion(?Share $share): string
    {
        $versions = $share?->releases()->pluck('version')->all() ?? [];
        $highest = collect($versions)->map(fn (string $version) => array_map('intval', explode('.', Str::before(Str::before($version, '+'), '-'))))
            ->sort(fn (array $a, array $b) => $b <=> $a)
            ->first();

        return $highest === null ? '1.0.0' : $highest[0].'.'.($highest[1] + 1).'.0';
    }

    /**
     * RFC 0085's dependency list, as TOML [[dependencies]] tables.
     *
     * @return list<array{id?: string, kind: string, min?: string, max?: string, any_of?: list<array<string, string>>}>
     *
     * @throws ShareError
     */
    public static function dependencies(string $toml): array
    {
        if (trim($toml) === '') {
            return [];
        }

        try {
            $document = TomlParser::decode($toml, asArray: true);
        } catch (Throwable) {
            throw new ShareError('invalid_request', 'The dependency list is not valid TOML.');
        }

        $entries = is_array($document) ? ($document['dependencies'] ?? []) : null;

        if (! is_array($entries) || ! array_is_list($entries)) {
            throw new ShareError('invalid_request', 'The dependency list must be an array of [[dependencies]] tables.');
        }

        $listings = collect(ContentIndex::snapshot()['listings'])->pluck('authored.type', 'id');

        return array_map(function ($entry) use ($listings) {
            $id = is_array($entry) ? ($entry['id'] ?? null) : null;

            if (! is_array($entry) || ! in_array($entry['kind'] ?? null, self::KINDS, true)
                || ($id !== null && (! is_string($id) || preg_match(self::ID, $id) !== 1))
                || ($id === null && ! isset($entry['any_of']))
                || array_diff(array_keys($entry), ['id', 'kind', 'min', 'max', 'any_of']) !== []) {
                throw new ShareError('invalid_request', 'Each dependency needs an id and a kind: required, optional, recommends, suggests or conflict.');
            }

            foreach (['min', 'max'] as $bound) {
                if (isset($entry[$bound]) && (! is_string($entry[$bound]) || preg_match(self::BOUND, $entry[$bound]) !== 1)) {
                    throw new ShareError('invalid_request', "The {$bound} of {$id} is not a version such as 1.4.0.");
                }
            }

            // RFC 0085: a listed id must be a mod; an unlisted one is fine.
            if ($id !== null && isset($listings[$id]) && $listings[$id] !== 'mod') {
                throw new ShareError('invalid_request', "{$id} is a {$listings[$id]}, and a vehicle or save can only depend on mods.");
            }

            return $entry;
        }, $entries);
    }

    /**
     * A new listing: its fields checked, its id free, and the account under its daily limit.
     *
     * @param  array<string, mixed>  $input
     *
     * @throws ShareError
     */
    private static function newListing(User $user, string $type, array $input): Share
    {
        $id = (string) ($input['id'] ?? '');
        $authors = self::list($input['authors'] ?? null);
        $tags = self::list($input['tags'] ?? null);

        if (preg_match(self::ID, $id) !== 1) {
            throw new ShareError('invalid_request', 'The id must be 1 to 64 letters, digits, ., _ or -, starting and ending with a letter or digit, such as ExampleRover.');
        }

        foreach (['name' => 'a name', 'abstract' => 'a one-line summary', 'license' => 'a license, such as CC-BY-4.0'] as $field => $what) {
            if (trim((string) ($input[$field] ?? '')) === '') {
                throw new ShareError('invalid_request', "The listing needs {$what}.");
            }
        }

        if ($authors === []) {
            throw new ShareError('invalid_request', 'The listing needs at least one author.');
        }

        if (preg_match('/^[A-Za-z0-9.:+()-]+(?: (?:AND|OR|WITH) [A-Za-z0-9.:+()-]+)*$/', (string) $input['license']) !== 1) {
            throw new ShareError('invalid_request', 'The license must be an SPDX expression, such as CC-BY-4.0 or MIT.');
        }

        foreach ($tags as $tag) {
            if (preg_match('/^[a-z0-9]+(?:-[a-z0-9]+)*$/', $tag) !== 1) {
                throw new ShareError('invalid_request', "The tag \"{$tag}\" must be lowercase words joined by -, such as rover.");
            }
        }

        self::checkId($id);

        $account = $user->share_account ?? tap('u_'.Str::random(12), fn (string $account) => $user->forceFill(['share_account' => $account])->save());
        $recent = Share::where('account', $account)->where('created_at', '>=', now()->subDay())->oldest()->get();
        $limit = (int) config('share.new_listings_per_day');

        if ($recent->count() >= $limit) {
            throw new ShareError('rate_limited', "An account can publish {$limit} new listings a day. Try again later, or publish a new version of an existing one.", 429, [
                'retry_after' => max(1, (int) now()->diffInSeconds($recent->first()->created_at->addDay())),
            ]);
        }

        $share = new Share([
            'type' => $type,
            'listing_id' => $id,
            'account' => $account,
            'name' => Str::limit(trim((string) $input['name']), 200, ''),
            'authors' => $authors,
            'abstract' => Str::limit(trim((string) $input['abstract']), 300, ''),
            'description' => trim((string) ($input['description'] ?? '')) ?: null,
            'license' => (string) $input['license'],
            'tags' => $tags,
            'dependencies' => [],
        ]);
        $share->user()->associate($user)->save();

        return $share;
    }

    /**
     * An id is refused when the index, its open pull requests or this host already has it
     * (RFC 0094, Owners). Ids share one namespace, compared without case.
     *
     * @throws ShareError
     */
    private static function checkId(string $id): void
    {
        $taken = collect([
            ...array_column(ContentIndex::snapshot()['listings'], 'id'),
            ...array_column(ContentIndex::shared('vehicle'), 'id'),
            ...array_column(ContentIndex::shared('save'), 'id'),
            ...ShareBot::idsInOpenPullRequests(),
        ])->map(fn (string $taken) => strtolower($taken));

        if ($taken->contains(strtolower($id)) || Share::whereRaw('lower(listing_id) = ?', [strtolower($id)])->exists()) {
            throw new ShareError('id_taken', "The id {$id} is already taken in the index. Choose another.", 409);
        }
    }

    /**
     * RFC 0085: install.folder may not equal, without case, that of another listing of the same type.
     */
    private static function checkFolder(ShareArchive $inspection, ?Share $share): void
    {
        if ($inspection->type === null) {
            return;
        }

        $folder = strtolower((string) $inspection->folder);
        $indexed = collect(ContentIndex::shared($inspection->type))
            ->reject(fn (array $listing) => $listing['id'] === $share?->listing_id)
            ->flatMap(fn (array $listing) => array_map(fn (array $release) => strtolower((string) data_get($release, 'install.folder')), $listing['releases'] ?? []));
        $hosted = ShareRelease::whereNull('taken_down_at')
            ->whereHas('share', fn ($query) => $query->where('type', $inspection->type)->when($share, fn ($query) => $query->whereKeyNot($share->id)))
            ->whereRaw('lower(folder) = ?', [$folder])
            ->exists();

        if ($hosted || $indexed->contains($folder)) {
            $inspection->checks[] = ['name' => 'folder', 'passed' => false, 'message' => "Another {$inspection->type} already installs as \"{$inspection->folder}\", so a player could not have both. Save it in the game under another name."];
            $inspection->error = 'invalid_archive';

            return;
        }

        $inspection->checks[] = ['name' => 'folder', 'passed' => true, 'message' => "No other {$inspection->type} installs as \"{$inspection->folder}\"."];
    }

    /**
     * The listing text the player confirms: the whole document for a new version, and for a new id
     * the part the upload decides, until the player fills in the rest.
     */
    private static function listingText(ShareUpload $upload): string
    {
        if ($upload->share !== null) {
            $share = $upload->share->replicate();
            $share->listing_id = $upload->share->listing_id;
            $share->dependencies = $upload->dependencies;

            return ListingDocument::toml($share);
        }

        return Toml::encode(['type' => $upload->type, 'dependencies' => $upload->dependencies]);
    }

    /**
     * A list field, given as a list or as comma-separated text, without blank entries.
     *
     * @return list<string>
     */
    private static function list(mixed $value): array
    {
        $items = is_array($value) ? $value : explode(',', is_scalar($value) ? (string) $value : '');

        return array_values(array_filter(array_map(fn ($item) => trim((string) $item), $items), fn (string $item) => $item !== ''));
    }

    private static function failure(ShareArchive $inspection): string
    {
        return collect($inspection->checks)->firstWhere('passed', false)['message'] ?? 'The archive failed a check.';
    }
}
