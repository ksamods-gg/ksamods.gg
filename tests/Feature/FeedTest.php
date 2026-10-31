<?php

use App\Models\Share;
use App\Models\ShareRelease;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;

uses(RefreshDatabase::class);

beforeEach(function () {
    Http::fake([ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => [[
        'id' => 'Rockets', 'published_at' => '2026-01-01T00:00:00Z', 'updated_at' => '2026-01-02T00:00:00Z',
        'authored' => ['type' => 'mod', 'name' => 'Rockets & Co', 'authors' => ['Maxi'], 'abstract' => 'Bigger rockets.'],
        'releases' => [
            ['version' => '1.0.0', 'release_status' => 'stable', 'release_date' => '2026-01-01T00:00:00Z', 'download' => ['url' => 'https://example.com/1.zip']],
            ['version' => '1.1.0', 'release_status' => 'stable', 'release_date' => '2026-01-02T00:00:00Z', 'download' => ['url' => 'https://example.com/2.zip'], 'changelog_text' => 'Fixed <staging>.'],
        ],
    ]]])]);
});

it('serves the mod releases as Atom, newest first, as valid XML', function () {
    $response = $this->get('/feeds/mods.atom')->assertOk()->assertHeader('Content-Type', 'application/atom+xml; charset=UTF-8');
    $feed = simplexml_load_string($response->getContent());

    expect((string) $feed->entry[0]->title)->toBe('Rockets & Co 1.1.0')
        ->and((string) $feed->entry[0]->summary)->toBe('Fixed <staging>.')
        ->and((string) $feed->entry[1]->title)->toBe('Rockets & Co 1.0.0');
});

it('serves one mod as RSS, and 404 for a mod the index does not have', function () {
    $feed = simplexml_load_string($this->get('/feeds/mods/Rockets.rss')->assertOk()->getContent());

    expect($feed->channel->item)->toHaveCount(2)
        ->and((string) $feed->channel->item[0]->guid)->toBe('tag:ksamods.gg,2026:mod/Rockets/1.1.0');

    $this->get('/feeds/mods/Nope.rss')->assertNotFound();
    $this->get('/feeds/mods.json')->assertNotFound();
});

it('serves shared vehicles and saves, without taken down releases', function () {
    $share = Share::factory()->create(['listing_id' => 'Rover', 'name' => 'Rover']);
    $share->releases()->save(new ShareRelease(['version' => '1.0.0', 'status' => 'stable', 'path' => 'a', 'size' => 1, 'sha256' => 'x', 'folder' => 'Rover', 'game_version' => '2026.1.1.1', 'dependencies' => []]));
    $share->releases()->save(new ShareRelease(['version' => '1.1.0', 'status' => 'stable', 'path' => 'b', 'size' => 1, 'sha256' => 'x', 'folder' => 'Rover', 'game_version' => '2026.1.1.1', 'dependencies' => [], 'taken_down_at' => now()]));

    $feed = simplexml_load_string($this->get('/feeds/shares.atom')->assertOk()->getContent());

    expect($feed->entry)->toHaveCount(1)->and((string) $feed->entry[0]->title)->toBe('Rover 1.0.0 (vehicle)');
});
