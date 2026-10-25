<?php

use App\Enums\Role;
use App\Models\Share;
use App\Models\ShareRelease;
use App\Models\User;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();
    Http::fake([
        ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => []]),
        '*snapshot-*' => Http::response(status: 404),
    ]);
});

function release(Share $share, array $attributes = []): ShareRelease
{
    return $share->releases()->create([
        'version' => '1.0.0', 'status' => 'stable', 'path' => 'archives/x.zip', 'size' => 1, 'sha256' => str_repeat('0', 64),
        'folder' => $share->name, 'game_version' => '2026.10.10.5554', 'dependencies' => [], ...$attributes,
    ]);
}

it('lists vehicles and saves in the sitemap, without ones whose releases were all taken down', function () {
    $live = Share::factory()->create();
    release($live);
    $gone = Share::factory()->create(['listing_id' => 'GoneRover', 'type' => 'save']);
    release($gone, ['taken_down_at' => now()]);

    $this->get('/sitemap.xml')
        ->assertOk()
        ->assertSee('<loc>'.route('shares.index', 'vehicles').'</loc>', false)
        ->assertSee('<loc>'.route('policy').'</loc>', false)
        ->assertSee('<loc>'.route('shares.show', ['types' => 'vehicles', 'id' => 'ExampleRover']).'</loc><lastmod>', false)
        ->assertDontSee('GoneRover');
});

it('keeps private pages out of search, and public ones in', function () {
    // Signed out: the sign-in pages. A private page redirects there instead, which is not indexed.
    foreach ([route('login'), '/oauth/device'] as $url) {
        expect($this->get($url)->assertOk()->headers->get('X-Robots-Tag'))->toBe('noindex, nofollow', $url);
    }

    $this->actingAs(User::factory()->create(['role' => Role::Admin]));
    foreach ([route('accounts'), route('notifications'), route('shares.create'), route('admin.dashboard')] as $url) {
        expect($this->get($url)->assertOk()->headers->get('X-Robots-Tag'))->toBe('noindex, nofollow', $url);
    }

    auth()->logout();
    foreach ([route('home'), route('mods.index'), route('shares.index', 'vehicles'), route('policy')] as $url) {
        expect($this->get($url)->assertOk()->headers->has('X-Robots-Tag'))->toBeFalse($url);
    }
});
