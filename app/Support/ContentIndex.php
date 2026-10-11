<?php

namespace App\Support;

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

    /**
     * Fresh for 10 minutes, then served stale for up to a day while it refreshes in the background.
     *
     * @return Snapshot
     */
    public static function snapshot(): array
    {
        return Cache::flexible('content-index:v1', [600, 86400], fn () => Http::timeout(15)
            ->retry(2, 500)
            ->get(self::URL)
            ->throw()
            ->json());
    }

    /**
     * Whether the index lists a mod or mod loader with this id.
     */
    public static function has(string $id): bool
    {
        return in_array($id, array_column(self::snapshot()['listings'], 'id'), true);
    }
}
