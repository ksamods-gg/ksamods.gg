<?php

namespace App\Http\Controllers;

use App\Models\Share;
use App\Models\ShareImage;
use App\Models\ShareRelease;
use App\Models\ShareUpload;
use App\Support\ContentIndex;
use App\Support\Share\DataFiles;
use App\Support\Share\Screenshots;
use App\Support\Share\ShareError;
use App\Support\Share\ShareHost;
use App\Support\Toml;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Http\UploadedFile;
use Illuminate\Validation\ValidationException;
use Inertia\Inertia;
use Inertia\Response;
use Symfony\Component\HttpFoundation\Response as HttpResponse;

/**
 * Vehicles and saves on ksamods.gg as a share host: browsing, each listing's page (its homepage in
 * the index), and the website's way to upload and publish, the same flow as the upload API.
 */
class ShareController extends Controller
{
    public function index(Request $request, string $types): Response
    {
        $type = array_search($types, Share::FOLDERS, true);
        $search = trim((string) $request->query('q'));

        return Inertia::render('Shares/Index', [
            'types' => $types,
            'search' => $search,
            'shares' => Share::where('type', $type)
                ->whereHas('releases', fn ($query) => $query->whereNull('taken_down_at'))
                ->when($search !== '', fn ($query) => $query->where('name', 'like', '%'.addcslashes($search, '%_\\').'%'))
                ->with(['releases' => fn ($query) => $query->whereNull('taken_down_at')])
                ->latest('updated_at')
                ->paginate(24)
                ->withQueryString()
                ->through(fn (Share $share) => $this->summary($share)),
        ]);
    }

    public function show(Request $request, string $types, string $id): Response
    {
        $share = Share::where('listing_id', $id)->where('type', array_search($types, Share::FOLDERS, true))->firstOrFail();
        $user = $request->user();
        $owns = $user !== null && $share->user_id === $user->id;

        return Inertia::render('Shares/Show', [
            'share' => [
                ...$this->summary($share),
                'description' => $share->description,
                'license' => $share->license,
                'dependencies' => $this->dependencies($share->dependencies),
                'pull_request_url' => $share->pull_request_url,
                'images' => $share->images()->get()->map(fn (ShareImage $image) => ['id' => $image->id, 'url' => $image->url()]),
                'releases' => $share->releases->reverse()->values()->map(fn (ShareRelease $release) => [
                    'id' => $release->id,
                    'version' => $release->version,
                    'status' => $release->status,
                    'size' => $release->size,
                    'sha256' => $release->sha256,
                    'game_version' => $release->game_version,
                    'date' => $release->created_at->toIso8601String(),
                    'url' => $release->taken_down_at ? null : $release->url(),
                    'taken_down' => $release->taken_down_at !== null,
                ])->filter(fn (array $release) => ! $release['taken_down'] || $owns || $user?->canModerate()),
            ],
            'owns' => $owns,
            'following' => $user?->isFollowing($share->type, $share->listing_id) ?? false,
            'canTakeDown' => $owns || ($user?->canModerate() ?? false),
            // Without asking GitHub: a merged pull request or the snapshot says it is listed.
            'canDelete' => $owns && $share->pull_request_state !== 'merged'
                && ! in_array($share->listing_id, array_column(ContentIndex::shared($share->type), 'id'), true),
        ]);
    }

    /**
     * The owner's form for the listing text: name, summary, description, license and tags.
     */
    public function edit(Request $request, string $types, string $id): Response
    {
        $share = $this->owned($request, $types, $id);

        return Inertia::render('Shares/Edit', [
            'share' => [
                'id' => $share->listing_id,
                'type' => $share->type,
                'name' => $share->name,
                'abstract' => $share->abstract,
                'description' => $share->description,
                'license' => $share->license,
                'tags' => implode(', ', $share->tags),
            ],
        ]);
    }

    /**
     * Saves the listing text and proposes it to the index, which takes it once its checks pass.
     */
    public function update(Request $request, string $types, string $id): RedirectResponse
    {
        $share = $this->owned($request, $types, $id);

        try {
            $pullRequest = ShareHost::amend($share, $request->only(['name', 'abstract', 'description', 'license', 'tags']));
        } catch (ShareError $error) {
            throw ValidationException::withMessages(['listing' => $error->getMessage()]);
        }

        Inertia::flash('status', $pullRequest
            ? __('Saved. The change is in the listing pull request; the index takes it once its checks pass.')
            : __('Saved here. GitHub could not be reached, so the pull request follows shortly.'));

        return to_route('shares.show', ['types' => $types, 'id' => $id]);
    }

    /**
     * Deletes the owner's listing for good, while it has not reached the index.
     */
    public function destroy(Request $request, string $types, string $id): RedirectResponse
    {
        $share = $this->owned($request, $types, $id);

        if (ShareHost::listed($share)) {
            throw ValidationException::withMessages(['listing' => __('This listing is in the index, so it cannot be deleted. Take its releases down instead.')]);
        }

        ShareHost::purge($share);
        Inertia::flash('status', __('Deleted :name.', ['name' => $share->name]));

        return to_route('shares.index', $types);
    }

    /**
     * Adds screenshots from the owner, one or more at once.
     */
    public function storeImages(Request $request, string $types, string $id): RedirectResponse
    {
        $share = $this->owned($request, $types, $id);
        $request->validate([
            'images' => ['required', 'array', 'min:1', 'max:'.Screenshots::MAX],
            'images.*' => ['file', 'max:'.(Screenshots::MAX_UPLOAD_BYTES / 1024)],
        ]);

        try {
            foreach ($request->file('images') as $file) {
                Screenshots::add($share, (string) $file->get());
            }
        } catch (ShareError $error) {
            throw ValidationException::withMessages(['images' => $error->getMessage()]);
        }

        return back();
    }

    /**
     * Removes a screenshot: its owner, or a moderator without taking a release down.
     */
    public function destroyImage(Request $request, ShareImage $image): RedirectResponse
    {
        abort_unless($image->share->user_id === $request->user()->id || $request->user()->canModerate(), 403);
        Screenshots::remove($image);

        return back();
    }

    public function coverImage(Request $request, ShareImage $image): RedirectResponse
    {
        abort_unless($image->share->user_id === $request->user()->id, 403);
        Screenshots::makeCover($image);

        return back();
    }

    private function owned(Request $request, string $types, string $id): Share
    {
        $share = Share::where('listing_id', $id)->where('type', array_search($types, Share::FOLDERS, true))->firstOrFail();
        abort_unless($share->user_id === $request->user()->id, 403);

        return $share;
    }

    /**
     * The upload page: a first-time notice, the player's listings to add a version to, and the mods
     * a dependency can name.
     */
    public function create(Request $request): Response
    {
        $user = $request->user();

        return Inertia::render('Shares/Create', [
            'consented' => $user->share_consented_at !== null,
            'listing' => $request->query('listing'),
            'mine' => $user->shares()->orderBy('name')->get(['listing_id', 'name', 'type'])->map(fn (Share $share) => [
                'id' => $share->listing_id, 'name' => $share->name, 'type' => $share->type,
            ]),
            'mods' => collect(ContentIndex::snapshot()['listings'])
                ->where('authored.type', 'mod')
                ->map(fn (array $listing) => ['id' => $listing['id'], 'name' => $listing['authored']['name']])
                ->sortBy('name', SORT_NATURAL | SORT_FLAG_CASE)
                ->values(),
            'maxBytes' => config('share.max_bytes'),
            'types' => config('share.types'),
        ]);
    }

    public function upload(Request $request): RedirectResponse
    {
        $user = $request->user();

        $request->validate([
            'archive' => ['required', 'file'],
            'listing' => ['nullable', 'string'],
            'dependencies' => ['array'],
            'dependencies.*.id' => ['required', 'string'],
            'dependencies.*.kind' => ['required', 'string'],
            'dependencies.*.min' => ['nullable', 'string'],
            'consent' => [$user->share_consented_at === null ? 'accepted' : 'nullable'],
        ], ['consent.accepted' => __('Confirm that you understand the listing becomes public and keeps its history.')]);

        if ($user->share_consented_at === null) {
            $user->forceFill(['share_consented_at' => now()])->save();
        }

        $archive = $request->file('archive');
        assert($archive instanceof UploadedFile);

        $dependencies = Toml::encode(['dependencies' => array_map(
            fn (array $dependency) => array_filter($dependency, fn ($value) => $value !== null && $value !== ''),
            array_values($request->input('dependencies', [])),
        )]);

        try {
            $upload = ShareHost::upload($user, $archive, $dependencies, $request->input('listing') ?: null, 'ksamods.gg');
        } catch (ShareError $error) {
            // The checks that ran go back to the page with the message, as JSON in their own key.
            throw ValidationException::withMessages([
                'archive' => $error->getMessage(),
                'checks' => (string) json_encode($error->extra['checks'] ?? []),
            ]);
        }

        return to_route('shares.review', $upload);
    }

    /**
     * The checks, the listing text and the proposed version, and the form to publish.
     */
    public function review(Request $request, ShareUpload $upload): Response
    {
        abort_unless($upload->user_id === $request->user()->id && $upload->published_at === null, 404);

        return Inertia::render('Shares/Review', [
            ...ShareHost::answer($upload),
            'expired' => $upload->expired(),
            'listingId' => $upload->share?->listing_id,
            'dependencies' => $this->dependencies($upload->dependencies),
            'defaults' => ['name' => $upload->folder, 'authors' => $request->user()->name],
        ]);
    }

    public function publish(Request $request, ShareUpload $upload): RedirectResponse
    {
        try {
            $result = ShareHost::publish($request->user(), $upload, $request->all());
        } catch (ShareError $error) {
            throw ValidationException::withMessages([$this->field($error->error) => $error->getMessage()]);
        }

        Inertia::flash('status', $result['pull_request']
            ? __('Published :version. The listing pull request is open; it merges by itself once its checks pass.', ['version' => $result['version']])
            : __('Published :version. The index picks it up on its next check.', ['version' => $result['version']]));

        return to_route('shares.show', ['types' => Share::FOLDERS[$result['type']], 'id' => $result['id']]);
    }

    /**
     * A release's url (rule 21): a redirect to the archive on the share disk, or 410 Gone once it
     * was taken down (rules 29 and 31). The url itself never changes or gets reused (rule 25).
     */
    public function download(string $type, string $id, string $version): HttpResponse
    {
        $release = ShareRelease::where('version', $version)
            ->whereHas('share', fn ($query) => $query->where('type', $type)->where('listing_id', $id))
            ->firstOrFail();

        // Cacheable by a CDN for ten minutes, so a takedown's 410 reaches cached copies within that.
        if ($release->taken_down_at !== null) {
            return response('This release was taken down.', 410)
                ->header('Content-Type', 'text/plain')
                ->header('Cache-Control', 'public, max-age=600');
        }

        return redirect()->away(DataFiles::url($release->path))->header('Cache-Control', 'public, max-age=600');
    }

    /**
     * Takes one release down (rules 17, 18 and 31): its archive and its Internet Archive mirror
     * are deleted, its url answers 410, and the release list drops it and is signed again. The
     * version is never given out again.
     */
    public function takeDown(Request $request, ShareRelease $release): RedirectResponse
    {
        // Moderators, and the owner for their own releases.
        abort_unless($request->user()->canModerate() || $release->share->user_id === $request->user()->id, 403);
        ShareHost::takeDown($release, $request->user());

        return back();
    }

    public function policy(): Response
    {
        return Inertia::render('Policy', [
            'abuseEmail' => config('share.abuse_email'),
            'discord' => config('share.discord'),
            'responseHours' => config('share.response_hours'),
        ]);
    }

    /**
     * What a card and the top of a page show.
     *
     * @return array<string, mixed>
     */
    public function summary(Share $share): array
    {
        $latest = $share->releases->whereNull('taken_down_at')->last();

        return [
            'id' => $share->listing_id,
            'type' => $share->type,
            'name' => $share->name,
            'authors' => $share->authors,
            'abstract' => $share->abstract,
            'tags' => $share->tags,
            'version' => $latest?->version,
            'game_version' => $latest?->game_version,
            // The cover's square crop, for rows and headers.
            'icon' => $share->icon['url'] ?? null,
            'download_url' => $latest?->url(),
            'updated_at' => $share->updated_at->toIso8601String(),
            'updated_ago' => $share->updated_at->diffForHumans(),
        ];
    }

    /**
     * Dependencies with the name and icon of each mod the index lists, in the shape the mod page's
     * dependency list takes.
     *
     * @param  list<array<string, mixed>>  $dependencies
     * @return list<array<string, mixed>>
     */
    private function dependencies(array $dependencies): array
    {
        $listings = collect(ContentIndex::snapshot()['listings'])->keyBy('id');

        return array_map(fn (array $dependency) => [
            'id' => $dependency['id'] ?? null,
            'kind' => $dependency['kind'],
            'min' => $dependency['min'] ?? null,
            'max' => $dependency['max'] ?? null,
            'name' => $listings[$dependency['id'] ?? '']['authored']['name'] ?? null,
            'icon' => $listings[$dependency['id'] ?? '']['authored']['images']['icon']['url'] ?? null,
        ], $dependencies);
    }

    /**
     * Which field of the publish form an error belongs to.
     */
    private function field(string $error): string
    {
        return match ($error) {
            'id_taken' => 'id',
            'version_taken' => 'version',
            default => 'publish',
        };
    }
}
