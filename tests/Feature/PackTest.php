<?php

use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();
    $version = fn (string $version, array $mods) => ['authored' => [
        'id' => 'flight-pack', 'type' => 'modpack', 'name' => 'Flight Pack', 'authors' => ['Beik'], 'version' => $version,
        'released_at' => '2026-09-23T20:00:00Z', 'abstract' => 'Mods for planning flights.', 'description' => "Read **this**.\n\n<script>alert(1)</script>",
        'license' => 'MIT', 'links' => ['forums' => 'https://forums.ahwoo.com/threads/1/'], 'compatibility' => ['game_min' => '2026.9.10.5438'],
        'mods' => $mods,
    ]];

    Http::fake([ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => [
        ['id' => 'DeltaVMap', 'published_at' => '2026-01-01T00:00:00Z', 'updated_at' => '2026-01-01T00:00:00Z', 'authored' => ['type' => 'mod', 'name' => 'Delta-V Map', 'abstract' => 'A subway map.'], 'releases' => []],
    ], 'packs' => [
        ['id' => 'flight-pack', 'published_at' => '2026-09-23T20:00:00Z', 'updated_at' => '2026-09-24T20:00:00Z', 'versions' => [
            $version('1.0.1', [['id' => 'DeltaVMap', 'version' => '1.2.6'], ['id' => 'Unlisted', 'version' => '0.1.0']]),
            $version('1.0.0', [['id' => 'DeltaVMap', 'version' => '1.2.5']]),
        ]],
        // No versions: left out rather than breaking the list.
        ['id' => 'broken-pack', 'versions' => []],
    ]])]);
});

it('lists the packs from the index, and leaves out one it cannot show', function () {
    $this->get(route('packs.index'))->assertOk()->assertInertia(fn (Assert $page) => $page
        ->component('Packs/Index')
        ->has('packs', 1)
        ->where('packs.0.id', 'flight-pack')
        ->where('packs.0.version', '1.0.1')
        ->where('packs.0.mod_count', 2));
});

it('shows a pack with its mods at their pinned versions, and its earlier versions', function () {
    $this->get(route('packs.show', 'flight-pack'))->assertOk()->assertInertia(fn (Assert $page) => $page
        ->component('Packs/Show')
        ->where('pack.mods.0', ['id' => 'DeltaVMap', 'version' => '1.2.6', 'name' => 'Delta-V Map', 'abstract' => 'A subway map.', 'icon' => null, 'listed' => true])
        ->where('pack.mods.1.listed', false)
        ->where('pack.versions.1.version', '1.0.0')
        ->where('pack.description', fn (string $html) => str_contains($html, '<strong>this</strong>') && ! str_contains($html, '<script>')));
});

it('answers 404 for a pack the index does not list, and lists packs in the sitemap', function () {
    $this->get(route('packs.show', 'nope'))->assertNotFound();
    $this->get('/sitemap.xml')->assertOk()->assertSee(route('packs.show', 'flight-pack'), false);
});
