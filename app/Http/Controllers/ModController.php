<?php

namespace App\Http\Controllers;

use App\Support\ContentIndex;
use App\Support\ListingOwnership;
use App\Support\ModSocial;
use Illuminate\Http\Request;
use Illuminate\Support\Arr;
use Illuminate\Support\Carbon;
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
    /**
     * The landing page: what ships, what people install and rate, and how to install or publish.
     */
    public function home(): Response
    {
        $snapshot = ContentIndex::snapshot();
        $listings = collect($snapshot['listings']);
        $ratings = ModSocial::ratings();
        $summaries = $listings->map(fn (array $listing) => [...$this->summary($listing), 'rating' => $ratings[$listing['id']] ?? null]);

        // Stable releases only: nightly builds would crowd out everything else.
        $releases = $listings
            ->flatMap(fn (array $listing) => collect($listing['releases'] ?? [])
                ->where('release_status', 'stable')
                ->map(fn (array $release) => [
                    'mod' => [
                        'id' => $listing['id'],
                        'name' => $listing['authored']['name'],
                        'icon' => $listing['authored']['images']['icon']['url'] ?? null,
                    ],
                    'version' => $release['version'],
                    'date' => $release['release_date'],
                    'ago' => Carbon::parse($release['release_date'])->diffForHumans(),
                    // The log groups releases by day.
                    'day' => $this->dayLabel(Carbon::parse($release['release_date'])),
                    'summary' => $this->firstLine($release['changelog_text'] ?? null),
                    'download_url' => $release['download']['url'],
                ]))
            ->sortByDesc('date')
            ->take(6)
            ->values();
        $popular = $summaries->sortByDesc('downloads')->take(6)->values();

        return Inertia::render('Home', [
            'releases' => $releases,
            'popular' => $popular,
            // Rated by enough people to mean something, and not a repeat of the shelf above.
            'topRated' => $summaries
                ->filter(fn (array $mod) => ($mod['rating']['count'] ?? 0) >= 3)
                ->whereNotIn('id', $popular->pluck('id'))
                ->sortBy([['rating.average', 'desc'], ['rating.count', 'desc']])
                ->take(3)
                ->values(),
            'counts' => [
                'mods' => $listings->count(),
                'downloads' => $listings->sum(fn (array $listing) => $listing['downloads']['total'] ?? 0),
            ],
            'community' => ModSocial::recent(4, $listings->mapWithKeys(fn (array $listing) => [$listing['id'] => $listing['authored']['name']])->all()),
        ]);
    }

    public function index(): Response
    {
        $snapshot = ContentIndex::snapshot();

        $ratings = ModSocial::ratings();

        return Inertia::render('Mods/Index', [
            'mods' => collect($snapshot['listings'])
                ->map(fn (array $listing) => [...$this->summary($listing), 'rating' => $ratings[$listing['id']] ?? null])
                ->values(),
            'tagNames' => $this->tagNames($snapshot),
            'gameVersions' => $this->gameVersions($snapshot),
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

        $ratings = ModSocial::ratings();

        // Kept for the public profile, which cannot ask GitHub on a visitor's behalf.
        $ids = array_map('strval', array_keys($roles));

        if ($ids !== ($user->mod_ids ?? [])) {
            $user->forceFill(['mod_ids' => $ids])->save();
        }

        return Inertia::render('Mods/Mine', [
            'mods' => collect($snapshot['listings'])
                ->filter(fn (array $listing) => isset($roles[$listing['id']]))
                ->map(fn (array $listing) => [
                    ...$this->summary($listing),
                    'rating' => $ratings[$listing['id']] ?? null,
                    'role' => $roles[$listing['id']],
                ])
                ->values(),
            'tagNames' => $this->tagNames($snapshot),
        ]);
    }

    public function show(Request $request, string $id): Response
    {
        $snapshot = ContentIndex::snapshot();
        $listings = collect($snapshot['listings']);
        $listing = $listings->firstWhere('id', $id) ?? abort(404);
        $authored = $listing['authored'];
        $images = collect($authored['images']['description'] ?? [])->pluck('url', 'id');
        $listingNames = $listings->mapWithKeys(fn (array $listing) => [$listing['id'] => $listing['authored']['name']]);
        $listingIcons = $listings->mapWithKeys(fn (array $listing) => [$listing['id'] => $listing['authored']['images']['icon']['url'] ?? null]);

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
                // Screenshots and other description images, for the gallery.
                'images' => $images->filter(fn (string $url) => preg_match('#^https?://#', $url) === 1)->values(),
                'downloads' => $listing['downloads']['total'] ?? 0,
                'published_at' => $listing['published_at'],
                'updated_at' => $listing['updated_at'],
                'releases' => $this->releases($listing)->map(fn (array $release) => [
                    'version' => $release['version'],
                    'status' => $release['release_status'],
                    'date' => $release['release_date'],
                    'game_min' => $release['game_min'] ?? null,
                    'game_min_revision' => $release['game_min_revision'] ?? null,
                    'download_url' => $release['download']['url'],
                    'download_size' => $release['download']['size'] ?? null,
                    'mirrors' => $release['download']['mirrors'] ?? [],
                    'loader' => isset($release['loader']) ? [
                        'id' => $release['loader']['id'],
                        'name' => $listingNames[$release['loader']['id']] ?? null,
                        'icon' => $listingIcons[$release['loader']['id']] ?? null,
                        'min' => $release['loader']['min'] ?? null,
                    ] : null,
                    // An entry names one mod, or with any_of several, any one of which will do (RFC 0031).
                    'dependencies' => collect($release['dependencies'] ?? [])->map(fn (array $dependency) => [
                        ...$this->relatedMod($dependency['id'] ?? null, $dependency, $listingNames, $listingIcons),
                        'kind' => $dependency['kind'],
                        'any_of' => isset($dependency['any_of'])
                            ? array_map(fn (array $alternative) => $this->relatedMod($alternative['id'], $alternative, $listingNames, $listingIcons), $dependency['any_of'])
                            : null,
                    ]),
                    'changelog' => isset($release['changelog_text'])
                        ? $this->markdown($release['changelog_text'], $images)
                        : null,
                    'changelog_url' => $release['changelog'] ?? null,
                ])->values(),
            ],
            // Listings whose newest release needs this one, as its loader or a required dependency,
            // with the versions of this one they accept.
            'dependents' => $listings
                ->map(function (array $other) use ($id) {
                    $latest = $this->latestRelease($other);
                    $needs = ($latest['loader']['id'] ?? null) === $id
                        ? ['kind' => 'loader', 'min' => $latest['loader']['min'] ?? null, 'max' => null]
                        : collect($latest['dependencies'] ?? [])
                            ->where('kind', 'required')
                            // This mod itself, or one of its alternatives, with the bounds that name it.
                            ->map(function (array $dependency) use ($id) {
                                $alternative = collect($dependency['any_of'] ?? [])->firstWhere('id', $id);

                                return match (true) {
                                    ($dependency['id'] ?? null) === $id => $dependency,
                                    $alternative !== null => [...$alternative, 'kind' => $dependency['kind']],
                                    default => null,
                                };
                            })
                            ->filter()
                            ->first();

                    return $needs === null ? null : [
                        'id' => $other['id'],
                        'name' => $other['authored']['name'],
                        'icon' => $other['authored']['images']['icon']['url'] ?? null,
                        'kind' => $needs['kind'],
                        'min' => $needs['min'] ?? null,
                        'max' => $needs['max'] ?? null,
                        'downloads' => $other['downloads']['total'] ?? 0,
                    ];
                })
                ->filter()
                ->sortByDesc('downloads')
                ->map(fn (array $dependent) => Arr::except($dependent, 'downloads'))
                ->values(),
            'tagNames' => $this->tagNames($snapshot),
            'gameVersions' => $this->gameVersions($snapshot),
            'following' => $request->user()?->isFollowing('mod', $id) ?? false,
            // The tab a link opens, such as a notification's link to a comment.
            'tab' => in_array($request->query('tab'), ['gallery', 'dependents', 'releases', 'reviews', 'comments'], true) ? $request->query('tab') : null,
            // Adds reviews and comments.
            ...ModSocial::forListing($id, $request->user()),
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
    public function summary(array $listing): array
    {
        $latest = $this->latestRelease($listing);

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
            // Server-side, so the server render and the browser agree.
            'updated_ago' => Carbon::parse($listing['updated_at'])->diffForHumans(),
            'version' => $latest['version'] ?? null,
            // What the newest release needs, for the mod list's filters.
            'loader' => $latest['loader']['id'] ?? null,
            'game_min' => $latest['game_min'] ?? null,
            'game_min_revision' => $latest['game_min_revision'] ?? null,
        ];
    }

    /**
     * A mod another one names, with its name and icon when the index lists it.
     *
     * @param  array{min?: string, max?: string}  $bounds
     * @param  Collection<string, string>  $names
     * @param  Collection<string, string|null>  $icons
     * @return array{id: string|null, name: string|null, icon: string|null, min: string|null, max: string|null}
     */
    private function relatedMod(?string $id, array $bounds, Collection $names, Collection $icons): array
    {
        return [
            'id' => $id,
            // The listed mod's name, or null when the index does not list it.
            'name' => $id === null ? null : ($names[$id] ?? null),
            'icon' => $id === null ? null : ($icons[$id] ?? null),
            'min' => $bounds['min'] ?? null,
            'max' => $bounds['max'] ?? null,
        ];
    }

    /**
     * Newest stable release, falling back to the newest of any status.
     *
     * @param  Listing  $listing
     * @return Release|null
     */
    private function latestRelease(array $listing): ?array
    {
        $releases = $this->releases($listing);

        return $releases->firstWhere('release_status', 'stable') ?? $releases->first();
    }

    /**
     * Builds the game has shipped, newest first, with the revision releases compare by. Month
     * forms such as 2026.7 carry no revision and are left out.
     *
     * @param  Snapshot  $snapshot
     * @return Collection<int, array{label: string, revision: int}>
     */
    private function gameVersions(array $snapshot): Collection
    {
        return collect(array_reverse($snapshot['game_versions']['versions'] ?? []))
            ->filter(fn (string $version) => preg_match('/^\d{4}\.\d+\.\d+\.\d+$/', $version) === 1)
            ->map(fn (string $version) => ['label' => $version, 'revision' => (int) Str::afterLast($version, '.')])
            ->values();
    }

    /**
     * @param  Snapshot  $snapshot
     * @return array<string, string>
     */
    public function tagNames(array $snapshot): array
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

    /**
     * A changelog's first line of substance, as plain text: headings and bare version lines are
     * skipped, as are compatibility notes, which say what a release needs rather than what it
     * changed; list markers, emphasis and links are stripped.
     */
    private function firstLine(?string $markdown): ?string
    {
        // UTF-8 mode throughout: without /u, \R also matches the byte 0x85, which is half of
        // characters such as Ņ (C5 85), and the cut text then fails to encode as JSON.
        foreach (preg_split('/\R/u', (string) $markdown) ?: [] as $line) {
            $line = trim($line);

            if ($line === '' || str_starts_with($line, '#') || preg_match('/^\[?v?\d+\.\d+/u', $line)) {
                continue;
            }

            $text = trim((string) preg_replace(
                ['/^([-*+]|\d+\.)\s+/u', '/!\[[^\]]*\]\([^)]*\)/u', '/\[([^\]]*)\]\([^)]*\)/u', '/[*_`]/u'],
                ['', '', '$1', ''],
                $line,
            ));

            if ($text !== '' && ! preg_match('/^(requires|tested on|compatible with)\s*:/iu', $text)) {
                return Str::limit($text, 200);
            }
        }

        return null;
    }

    /**
     * "Today", "Yesterday", or the date, as the release log's day markers read.
     */
    private function dayLabel(Carbon $date): string
    {
        return match (true) {
            $date->isToday() => 'Today',
            $date->isYesterday() => 'Yesterday',
            default => $date->format('M j'),
        };
    }
}
