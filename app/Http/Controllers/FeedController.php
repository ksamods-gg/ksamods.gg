<?php

namespace App\Http\Controllers;

use App\Models\ShareRelease;
use App\Support\ContentIndex;
use Illuminate\Http\Response;
use Illuminate\Support\Carbon;
use Illuminate\Support\Collection;
use Illuminate\Support\Str;

/**
 * Atom and RSS feeds of new releases: every mod, one mod, and the vehicles and saves shared here.
 * Feed readers and Discord bots poll them, so they are plain and cached briefly.
 *
 * @phpstan-import-type Listing from ContentIndex
 *
 * @phpstan-type Item array{title: string, url: string, id: string, date: Carbon, authors: list<string>, summary: string}
 */
class FeedController extends Controller
{
    private const ITEMS = 50;

    public function mods(string $format): Response
    {
        $items = collect(ContentIndex::snapshot()['listings'])
            ->flatMap(fn (array $listing) => $this->releases($listing));

        return $this->feed($format, 'Kitten Space Agency mods: new releases', route('mods.index'), $items);
    }

    public function mod(string $id, string $format): Response
    {
        $listing = collect(ContentIndex::snapshot()['listings'])->firstWhere('id', $id) ?? abort(404);

        return $this->feed($format, "{$listing['authored']['name']}: new releases", route('mods.show', $id), $this->releases($listing));
    }

    public function shares(string $format): Response
    {
        $items = ShareRelease::with('share')
            ->whereNull('taken_down_at')
            ->latest('id')
            ->limit(self::ITEMS)
            ->get()
            ->map(fn (ShareRelease $release) => [
                'title' => "{$release->share->name} {$release->version} ({$release->share->type})",
                'url' => $release->share->url(),
                'id' => "tag:ksamods.gg,2026:{$release->share->type}/{$release->share->listing_id}/{$release->version}",
                'date' => $release->created_at,
                'authors' => $release->share->authors,
                'summary' => $release->share->abstract,
            ]);

        return $this->feed($format, 'Kitten Space Agency vehicles and saves: new releases', route('shares.index', 'vehicles'), $items);
    }

    /**
     * @param  Listing  $listing
     * @return Collection<int, Item>
     */
    private function releases(array $listing): Collection
    {
        $authored = $listing['authored'];

        return collect($listing['releases'] ?? [])->map(fn (array $release) => [
            'title' => "{$authored['name']} {$release['version']}",
            'url' => route('mods.show', ['id' => $listing['id'], 'tab' => 'releases']),
            'id' => "tag:ksamods.gg,2026:mod/{$listing['id']}/{$release['version']}",
            'date' => Carbon::parse($release['release_date']),
            'authors' => $authored['authors'] ?? [],
            'summary' => Str::limit(trim($release['changelog_text'] ?? $authored['abstract'] ?? ''), 2000),
        ]);
    }

    /**
     * @param  string  $format  atom or rss, as the route allows.
     * @param  iterable<Item>  $items
     */
    private function feed(string $format, string $title, string $alternate, iterable $items): Response
    {
        $items = collect($items)->sortByDesc(fn (array $item) => $item['date']->getTimestamp())->take(self::ITEMS)->values();

        return response()->view("feeds.{$format}", [
            'title' => $title,
            'self' => url()->current(),
            'alternate' => $alternate,
            'updated' => $items->first()['date'] ?? now(),
            'items' => $items,
        ])->header('Content-Type', $format === 'atom' ? 'application/atom+xml; charset=UTF-8' : 'application/rss+xml; charset=UTF-8')
            ->header('Cache-Control', 'public, max-age=600');
    }
}
