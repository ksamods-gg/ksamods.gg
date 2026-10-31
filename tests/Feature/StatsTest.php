<?php

use App\Models\Review;
use App\Models\User;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();
    Http::fake([ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => [[
        'id' => 'Rockets', 'published_at' => '2026-01-01T00:00:00Z', 'updated_at' => '2026-01-01T00:00:00Z',
        'authored' => ['type' => 'mod', 'name' => 'Rockets'], 'releases' => [], 'downloads' => ['total' => 120],
    ]]])]);
});

it('notes each listing\'s downloads once a day', function () {
    $this->artisan('stats:record')->assertSuccessful();
    $this->artisan('stats:record')->assertSuccessful();

    expect(DB::table('download_stats')->get(['listing_id', 'downloads'])->map(fn ($row) => (array) $row)->all())
        ->toBe([['listing_id' => 'Rockets', 'downloads' => 120]]);
});

it('shows the download history with today\'s total, and the rating after each day of reviews', function () {
    DB::table('download_stats')->insert(['listing_id' => 'Rockets', 'date' => now()->subDays(2)->toDateString(), 'downloads' => 100]);
    [$a, $b] = User::factory()->count(2)->create();
    (new Review)->forceFill(['user_id' => $a->id, 'listing_id' => 'Rockets', 'rating' => 5, 'created_at' => now()->subDay()])->save();
    (new Review)->forceFill(['user_id' => $b->id, 'listing_id' => 'Rockets', 'rating' => 2])->save();

    $this->get(route('mods.show', 'Rockets'))->assertInertia(fn (Assert $page) => $page
        ->where('stats.downloads.0.value', 100)
        ->where('stats.downloads.1', ['date' => now()->toDateString(), 'value' => 120])
        ->where('stats.rating.0.value', 5)
        ->where('stats.rating.1.value', 3.5)
        ->where('stats.rating.1.count', 2));
});
