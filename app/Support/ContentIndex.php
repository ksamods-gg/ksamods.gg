<?php

namespace App\Support;

use Illuminate\Contracts\Cache\Repository;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\Http;

/**
 * The KSA content index snapshot. Shape of the parts this app reads (RFC 0033):
 *
 * @phpstan-type Release array{version: string, release_status: string, release_date: string, game_min?: string, game_min_revision?: int, download: array{url: string, size?: int, mirrors?: list<string>}, loader?: array{id: string, min?: string}, dependencies?: list<array{id: string, kind: string, min?: string, max?: string}>, changelog?: string, changelog_text?: string}
 * @phpstan-type Listing array{id: string, published_at: string, updated_at: string, authored: array{type: string, name: string, authors?: list<string>, abstract?: string, description?: string, license?: string, tags?: list<string>, links?: array<string, string>, images?: array{icon?: array{url: string}, description?: list<array{id: string, url: string}>}}, releases?: list<Release>, downloads?: array{total: int}}
 * @phpstan-type Snapshot array{listings: list<Listing>, tags: array{mod?: list<array{tag: string, name: string}>}, game_versions?: array{versions: list<string>}}
 */
class ContentIndex
{
    public const URL = 'https://ksamodding.github.io/content-index-releases/v1/index.json';

    public const REPOSITORY = 'KSAModding/content-index';

    private const CACHE_KEY = 'content-index:v1';

    /**
     * Fresh for 10 minutes, then served stale for up to a day while it refreshes in the background.
     */
    private const CACHE_TTL = [600, 86400];

    /**
     * @return Snapshot
     */
    public static function snapshot(): array
    {
        return self::cache()->flexible(self::CACHE_KEY, self::CACHE_TTL, fn () => self::fetch());
    }

    /**
     * Fetches the index now and replaces the cached copy. If the fetch fails, the cache is untouched.
     *
     * @return Snapshot
     */
    public static function refresh(): array
    {
        $snapshot = self::fetch();
        self::cache()->forget(self::CACHE_KEY);

        return self::cache()->flexible(self::CACHE_KEY, self::CACHE_TTL, fn () => $snapshot);
    }

    /**
     * @return Snapshot
     */
    private static function fetch(): array
    {
        return Http::timeout(15)->retry(2, 500)->get(self::URL)->throw()->json();
    }

    /**
     * The listings of a vehicle or save snapshot file (RFC 0094, Snapshot size), which hold the
     * entry shape of `listings`. Empty while the index publishes no such file yet.
     *
     * @param  'vehicle'|'save'  $type
     * @return list<Listing>
     */
    public static function shared(string $type): array
    {
        $file = $type === 'vehicle' ? 'vehicles' : 'saves';

        return self::cache()->flexible("content-index:v1:{$file}", self::CACHE_TTL, function () use ($file) {
            $response = Http::timeout(15)->retry(2, 500, throw: false)
                ->get(str_replace('index.json', "snapshot-{$file}.json", self::URL));

            return $response->notFound() ? [] : ($response->throw()->json($file) ?? []);
        });
    }

    /**
     * Where the snapshots are cached: a store of their own, the file cache unless
     * CONTENT_INDEX_CACHE_STORE says otherwise. The snapshot is well over a megabyte and growing,
     * more than a database cache row takes on a server with a small max_allowed_packet.
     */
    private static function cache(): Repository
    {
        return Cache::store(config('cache.content_index_store'));
    }

    /**
     * Whether the index lists a mod or mod loader with this id.
     */
    public static function has(string $id): bool
    {
        return in_array($id, array_column(self::snapshot()['listings'], 'id'), true);
    }
}
