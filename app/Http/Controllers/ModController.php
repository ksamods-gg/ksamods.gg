<?php

namespace App\Http\Controllers;

use App\Support\ContentIndex;
use App\Support\ListingOwnership;
use Illuminate\Http\Request;
use Illuminate\Support\Collection;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Str;
use Inertia\Inertia;
use Inertia\Response;

/**
 * @phpstan-import-type Listing from ContentIndex
 * @phpstan-import-type Release from ContentIndex
 * @phpstan-import-type Snapshot from ContentIndex
 */
class ModController extends Controller
{
    public function index(): Response
    {
        $snapshot = ContentIndex::snapshot();

        return Inertia::render('Mods/Index', [
            'mods' => collect($snapshot['listings'])->map($this->summary(...))->values(),
            'tagNames' => $this->tagNames($snapshot),
        ]);
    }

    /**
     * The listings the user controls the release repository of, the same check that gates editing.
     */
    public function mine(Request $request): Response
    {
        $user = $request->user();
        $snapshot = ContentIndex::snapshot();

        // ponytail: one GitHub lookup per listing, concurrent and cached per user; past a few
        // hundred listings, ask GitHub for the user's own repositories instead.
        $roles = Cache::remember("my-mods:{$user->id}", 300, fn () => ListingOwnership::roles(
            $user,
            collect($snapshot['listings'])
                ->mapWithKeys(fn (array $listing) => [$listing['id'] => ListingOwnership::repository($listing['authored'])])
                ->filter()
                ->all(),
        ));

        return Inertia::render('Mods/Mine', [
            'mods' => collect($snapshot['listings'])
                ->filter(fn (array $listing) => isset($roles[$listing['id']]))
                ->map(fn (array $listing) => [...$this->summary($listing), 'role' => $roles[$listing['id']]])
                ->values(),
            'tagNames' => $this->tagNames($snapshot),
        ]);
    }

    public function show(string $id): Response
    {
        $snapshot = ContentIndex::snapshot();
        $listings = collect($snapshot['listings']);
        $listing = $listings->firstWhere('id', $id) ?? abort(404);
        $authored = $listing['authored'];
        $images = collect($authored['images']['description'] ?? [])->pluck('url', 'id');
        $listingIds = $listings->pluck('id')->flip();

        return Inertia::render('Mods/Show', [
            'mod' => [
                'id' => $listing['id'],
                'type' => $authored['type'],
                'name' => $authored['name'],
                'authors' => $authored['authors'] ?? [],
                'abstract' => $authored['abstract'] ?? null,
                'description' => $this->markdown($authored['description'] ?? '', $images),
                'license' => $authored['license'] ?? null,
                'tags' => $authored['tags'] ?? [],
                'icon' => $authored['images']['icon']['url'] ?? null,
                'links' => $authored['links'] ?? [],
                'downloads' => $listing['downloads']['total'] ?? 0,
                'published_at' => $listing['published_at'],
                'updated_at' => $listing['updated_at'],
                'releases' => $this->releases($listing)->map(fn (array $release) => [
                    'version' => $release['version'],
                    'status' => $release['release_status'],
                    'date' => $release['release_date'],
                    'game_min' => $release['game_min'] ?? null,
                    'download_url' => $release['download']['url'],
                    'download_size' => $release['download']['size'] ?? null,
                    'mirrors' => $release['download']['mirrors'] ?? [],
                    'loader' => isset($release['loader']) ? [
                        'id' => $release['loader']['id'],
                        'min' => $release['loader']['min'] ?? null,
                    ] : null,
                    'dependencies' => collect($release['dependencies'] ?? [])->map(fn (array $dependency) => [
                        'id' => $dependency['id'],
                        'kind' => $dependency['kind'],
                        'listed' => $listingIds->has($dependency['id']),
                    ]),
                    'changelog' => isset($release['changelog_text'])
                        ? $this->markdown($release['changelog_text'], $images)
                        : null,
                    'changelog_url' => $release['changelog'] ?? null,
                ])->values(),
            ],
            'tagNames' => $this->tagNames($snapshot),
        ]);
    }

    /**
     * @param  Listing  $listing
     * @return Collection<int, Release>
     */
    private function releases(array $listing): Collection
    {
        return collect($listing['releases'] ?? [])->sortByDesc('release_date')->values();
    }

    /**
     * What a mod card shows.
     *
     * @param  Listing  $listing
     * @return array<string, mixed>
     */
    private function summary(array $listing): array
    {
        return [
            'id' => $listing['id'],
            'type' => $listing['authored']['type'],
            'name' => $listing['authored']['name'],
            'authors' => $listing['authored']['authors'] ?? [],
            'abstract' => $listing['authored']['abstract'] ?? null,
            'tags' => $listing['authored']['tags'] ?? [],
            'icon' => $listing['authored']['images']['icon']['url'] ?? null,
            'downloads' => $listing['downloads']['total'] ?? 0,
            'updated_at' => $listing['updated_at'],
            'version' => $this->latestVersion($listing),
        ];
    }

    /**
     * Newest stable release, falling back to the newest of any status.
     *
     * @param  Listing  $listing
     */
    private function latestVersion(array $listing): ?string
    {
        $releases = $this->releases($listing);

        return ($releases->firstWhere('release_status', 'stable') ?? $releases->first())['version'] ?? null;
    }

    /**
     * @param  Snapshot  $snapshot
     * @return array<string, string>
     */
    private function tagNames(array $snapshot): array
    {
        return collect($snapshot['tags']['mod'] ?? [])->pluck('name', 'tag')->all();
    }

    /**
     * Index content is third-party, so raw HTML and unsafe links are stripped.
     *
     * @param  Collection<string, string>  $images
     */
    private function markdown(string $text, Collection $images): string
    {
        $text = preg_replace_callback(
            '/ksa-image:([\w.-]+)/',
            fn (array $match) => $images[$match[1]] ?? $match[0],
            $text,
        );

        return Str::markdown($text, ['html_input' => 'strip', 'allow_unsafe_links' => false]);
    }
}
