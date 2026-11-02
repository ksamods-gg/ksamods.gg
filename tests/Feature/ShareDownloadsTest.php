<?php

use App\Models\Share;
use Illuminate\Foundation\Testing\RefreshDatabase;

uses(RefreshDatabase::class);

function downloadableShare(array $attributes = [], ?array $release = []): Share
{
    $share = Share::factory()->create($attributes);
    $share->releases()->create([
        'version' => '1.0.0', 'status' => 'stable', 'path' => 'archives/x.zip', 'size' => 1, 'sha256' => str_repeat('0', 64),
        'folder' => $share->name, 'game_version' => '2026.10.10.5554', 'dependencies' => [], ...$release,
    ]);

    return $share;
}

it('counts a player once a day, without touching when the listing changed', function () {
    $share = downloadableShare(['updated_at' => now()->subWeek()]);
    $url = route('share.api.downloads', ['type' => $share->type, 'id' => $share->listing_id]);

    $this->post($url)->assertNoContent();
    $this->post($url)->assertNoContent();
    $this->withServerVariables(['REMOTE_ADDR' => '10.0.0.2'])->post($url)->assertNoContent();

    expect($share->fresh())
        ->downloads->toBe(2)
        ->updated_at->toDateString()->toBe(now()->subWeek()->toDateString());

    // The next day, the same player counts again.
    $this->travel(1)->day();
    $this->post($url)->assertNoContent();
    expect($share->fresh()->downloads)->toBe(3);
});

it('does not count a listing with nothing left to download', function () {
    $share = downloadableShare(release: ['taken_down_at' => now()]);

    $this->post(route('share.api.downloads', ['type' => $share->type, 'id' => $share->listing_id]))->assertNotFound();
    expect($share->fresh()->downloads)->toBe(0);
});

it('orders the list by downloads when asked', function () {
    $this->withoutVite();
    downloadableShare(['listing_id' => 'Quiet', 'name' => 'Quiet']);
    downloadableShare(['listing_id' => 'Popular', 'name' => 'Popular', 'downloads' => 9, 'updated_at' => now()->subYear()]);

    $this->get(route('shares.index', ['types' => 'vehicles', 'sort' => 'downloads']))
        ->assertInertia(fn ($page) => $page
            ->where('sort', 'downloads')
            ->where('shares.data.0.id', 'Popular')
            ->where('shares.data.0.downloads', 9));

    $this->get(route('shares.index', 'vehicles'))
        ->assertInertia(fn ($page) => $page->where('shares.data.0.id', 'Quiet'));
});
