<?php

use App\Models\Share;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();
    // KSA 2026.11.10.5801 renamed part ids, so the stewards capped the older vehicles at 5707.
    Http::fake([
        ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => [],
            'game_versions' => ['versions' => ['2026.10.25.5707', '2026.11.10.5801']]]),
        '*snapshot-vehicles.json' => Http::response(['vehicles' => [
            ['id' => 'OldRover', 'releases' => [['version' => '1.0.0', 'game_min' => '2026.9.7.5402', 'game_max' => '2026.10.25.5707']]],
            ['id' => 'NewRover', 'releases' => [['version' => '1.0.0', 'game_min' => '2026.11.10.5801']]],
        ]]),
        '*snapshot-saves.json' => Http::response(status: 404),
    ]);

    foreach (['OldRover' => '2026.9.7.5402', 'NewRover' => '2026.11.10.5801'] as $id => $build) {
        Share::factory()->create(['listing_id' => $id, 'name' => $id])->releases()->create([
            'version' => '1.0.0', 'status' => 'stable', 'path' => 'archives/x.zip', 'size' => 1, 'sha256' => str_repeat('0', 64),
            'folder' => $id, 'game_version' => $build, 'dependencies' => [],
        ]);
    }
});

it('shows the cap the index set, and marks releases the current game build no longer loads', function () {
    $this->get(route('shares.index', 'vehicles'))->assertInertia(fn (Assert $page) => $page
        ->where('shares.data', fn ($shares) => collect($shares)->keyBy('id')->map(fn ($share) => [$share['game_max'], $share['outdated']])->all() === [
            'OldRover' => ['2026.10.25.5707', true],
            'NewRover' => [null, false],
        ]));

    $this->get(route('shares.show', ['types' => 'vehicles', 'id' => 'OldRover']))->assertInertia(fn (Assert $page) => $page
        ->where('share.outdated', true)
        ->where('share.releases.0.game_max', '2026.10.25.5707')
        ->where('share.releases.0.outdated', true));
});

it('does not mark a release capped at the current build', function () {
    expect(ContentIndex::beforeCurrentGame('2026.11.10.5801'))->toBeFalse()
        ->and(ContentIndex::beforeCurrentGame('2026.10.25.5707'))->toBeTrue()
        ->and(ContentIndex::beforeCurrentGame(null))->toBeFalse();
});
