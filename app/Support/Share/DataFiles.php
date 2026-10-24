<?php

namespace App\Support\Share;

use App\Models\Share;
use Illuminate\Contracts\Filesystem\Filesystem;
use Illuminate\Support\Facades\Storage;

/**
 * The host's data files (RFC 0094): the release list and owner record of each listing, and one
 * attestation per change it opens a pull request for. Each is UTF-8 JSON without a byte order
 * mark (rule 6), written with its signature beside it (rule 22), and served publicly by the
 * share disk at `data`, the public URL of config/share.php.
 */
class DataFiles
{
    /**
     * releases/<type>/<id>.json: every release not taken down, oldest first. The list only grows,
     * except that a takedown removes its entry (rules 10 and 18).
     */
    public static function writeReleaseList(Share $share): void
    {
        self::write("releases/{$share->type}/{$share->listing_id}.json", [
            'id' => $share->listing_id,
            'type' => $share->type,
            'releases' => $share->releases()->whereNull('taken_down_at')->get()->map(fn ($release) => [
                'version' => $release->version,
                'status' => $release->status,
                'url' => $release->url(),
                // When the archive first became downloadable, which never changes (rule 21).
                'date' => $release->created_at->utc()->format('Y-m-d\TH:i:s\Z'),
            ])->all(),
        ]);
    }

    /**
     * owners/<type>/<id>.json: the opaque account, and the GitHub id the owner signed in with,
     * which an erasure drops (rule 32).
     */
    public static function writeOwnerRecord(Share $share): void
    {
        $githubId = $share->user?->github_id;

        self::write("owners/{$share->type}/{$share->listing_id}.json", array_filter([
            'id' => $share->listing_id,
            'type' => $share->type,
            'account' => $share->account,
            'github_id' => $githubId !== null ? (int) $githubId : null,
        ], fn ($value) => $value !== null));
    }

    /**
     * attestations/<sha256>.json: the host vouches that its accounts made this exact change.
     *
     * @param  array<string, string>  $accounts  Each listing the change touches, as type/id, with its owner's account.
     */
    public static function writeAttestation(string $digest, array $accounts): void
    {
        self::write("attestations/{$digest}.json", [
            'host' => config('share.key'),
            'accounts' => $accounts,
            'created' => now()->utc()->format('Y-m-d\TH:i:s\Z'),
        ]);
    }

    /**
     * Writes one data file and its signature, both public. The file goes first, so no reader
     * sees a signature without its file; until the new signature lands, the old one fails, which
     * the index reads as "not there yet" rather than a different answer.
     *
     * @param  array<string, mixed>  $data
     */
    public static function write(string $path, array $data): void
    {
        $bytes = json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR)."\n";
        $options = ['visibility' => 'public', 'CacheControl' => 'max-age=30'];

        self::disk()->put($path, $bytes, [...$options, 'ContentType' => 'application/json']);
        self::disk()->put("{$path}.sig", Signer::sign($bytes)."\n", [...$options, 'ContentType' => 'text/plain']);
    }

    public static function read(string $path): ?string
    {
        return self::disk()->get($path);
    }

    public static function url(string $path): string
    {
        return config('share.public_url').$path;
    }

    public static function disk(): Filesystem
    {
        return Storage::disk(config('share.disk'));
    }
}
