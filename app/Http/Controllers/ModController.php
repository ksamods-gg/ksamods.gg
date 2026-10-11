<?php

namespace App\Http\Controllers;

use App\Support\ContentIndex;
use Illuminate\Support\Collection;
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
            'mods' => collect($snapshot['listings'])->map(fn (array $listing) => [
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
            ])->values(),
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
