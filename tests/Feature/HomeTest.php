<?php

use App\Models\Comment;
use App\Models\Review;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\Client\Factory;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();
    $this->travelTo('2026-10-11 12:00:00');

    $release = fn (string $version, string $status, string $date, ?string $changelog = null) => [
        'version' => $version,
        'release_status' => $status,
        'release_date' => $date,
        'download' => ['url' => "https://example.com/{$version}.zip"],
        'changelog_text' => $changelog,
    ];
    $listing = fn (string $id, int $downloads, array $releases) => [
        'id' => $id,
        'published_at' => '2026-01-01T00:00:00Z',
        'updated_at' => '2026-10-01T00:00:00Z',
        'authored' => ['type' => 'mod', 'name' => "{$id} Mod"],
        'releases' => $releases,
        'downloads' => ['total' => $downloads],
    ];

    Http::fake([ContentIndex::URL => Http::response([
        'tags' => ['mod' => []],
        'listings' => [
            $listing('Rockets', 500, [
                $release('2.0.0-nightly', 'dev', '2026-10-11T10:00:00Z', 'Nightly build.'),
                $release('1.1.0', 'stable', '2026-10-10T12:00:00Z', "## Changes\n\n- **Boostback:** now steers to the [pad](https://example.com).\n- More"),
            ]),
            $listing('Wheels', 9000, [
                $release('0.3.0', 'stable', '2026-10-09T12:00:00Z', "## [0.3.0](https://example.com) (2026-10-09)\n\n**Requires:** KSA v2026.10.7\n\n### Fixes\n\n* Fixed the axle."),
            ]),
        ],
    ])]);
});

it('shows a log of the newest stable releases, with a plain-text summary of each', function () {
    $this->get(route('home'))->assertInertia(fn (Assert $page) => $page
        ->component('Home')
        ->has('releases', 2)
        ->where('releases.0.mod', ['id' => 'Rockets', 'name' => 'Rockets Mod', 'icon' => null])
        ->where('releases.0.version', '1.1.0')
        ->where('releases.0.ago', '1 day ago')
        ->where('releases.0.day', 'Yesterday')
        ->where('releases.1.day', 'Oct 9')
        ->where('releases.0.summary', 'Boostback: now steers to the pad.')
        ->where('releases.1.summary', 'Fixed the axle.')
        ->where('counts', ['mods' => 2, 'downloads' => 9500])
        ->where('popular.0.id', 'Wheels'));
});

it('shows recent posts, linking to each, and rates nothing on one review', function () {
    Review::factory()->create(['listing_id' => 'Wheels', 'rating' => 5, 'body' => 'Great wheels']);
    $comment = Comment::factory()->create(['listing_id' => 'Rockets', 'body' => 'Love it', 'created_at' => now()->subMinute()]);
    // A post about a mod the index no longer lists is left out.
    Comment::factory()->create(['listing_id' => 'Gone', 'body' => 'Orphan']);

    $this->get(route('home'))->assertInertia(fn (Assert $page) => $page
        // One review is too few, and both mods already sit on the Most installed shelf.
        ->where('topRated', [])
        ->has('community', 2)
        ->where('community.0.excerpt', 'Great wheels')
        ->where('community.0.rating', 5)
        ->where('community.1.mod', ['id' => 'Rockets', 'name' => 'Rockets Mod'])
        ->where('community.1.url', route('mods.show', ['id' => 'Rockets', 'tab' => 'comments'])."#comment-{$comment->id}"));
});

it('keeps the full mod list at /mods', function () {
    $this->get('/mods')->assertInertia(fn (Assert $page) => $page->component('Mods/Index')->has('mods', 2));
});

it('keeps characters whole in a summary, so the page encodes as JSON', function () {
    // Ņ is C5 85 in UTF-8; matching \R byte by byte splits it at 0x85.
    Http::swap(new Factory);
    Http::fake([ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => [[
        'id' => 'Latvija',
        'published_at' => '2026-01-01T00:00:00Z',
        'updated_at' => '2026-10-01T00:00:00Z',
        'authored' => ['type' => 'mod', 'name' => 'Latvija'],
        'releases' => [[
            'version' => '1.0.0',
            'release_status' => 'stable',
            'release_date' => '2026-10-10T12:00:00Z',
            'download' => ['url' => 'https://example.com/1.0.0.zip'],
            'changelog_text' => "## 1.0.0\n\n- Pievienota ŅŪ raķete.",
        ]],
    ]]])]);

    $this->get(route('home'))->assertInertia(fn (Assert $page) => $page
        ->where('releases.0.summary', 'Pievienota ŅŪ raķete.'));

    // The page data in the HTML, which was empty when it failed to encode.
    $this->get(route('home'))->assertSee('"component":"Home"', false);
});
