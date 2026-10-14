<?php

use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;

// The mod pages read ratings, reviews and comments from the database.
uses(RefreshDatabase::class);

beforeEach(fn () => $this->withoutVite());

function fakeContentIndex(): void
{
    $release = fn (string $version, string $status, string $date) => [
        'version' => $version,
        'release_status' => $status,
        'release_date' => $date,
        'download' => ['url' => "https://example.com/{$version}.zip", 'size' => 1024],
        'dependencies' => [['id' => 'Loader', 'kind' => 'required', 'min' => '0.4'], ['id' => 'Unlisted', 'kind' => 'optional']],
        'changelog_text' => '## Changes',
        'game_min' => '2026.9.22.5482',
        'game_min_revision' => 5482,
        'loader' => ['id' => 'StarMap', 'min' => '0.4.5'],
    ];

    Http::fake([ContentIndex::URL => Http::response([
        'tags' => ['mod' => [['tag' => 'gameplay', 'name' => 'Gameplay']]],
        'game_versions' => ['versions' => ['2026.7', '2026.9.22.5482', '2026.10.7.5541']],
        'listings' => [
            [
                'id' => 'Rockets',
                'published_at' => '2026-01-01T00:00:00Z',
                'updated_at' => '2026-03-01T00:00:00Z',
                'authored' => [
                    'type' => 'mod',
                    'name' => 'Rockets',
                    'authors' => ['Jeb'],
                    'tags' => ['gameplay'],
                    'description' => "![Shot](ksa-image:shot)\n\n<script>alert(1)</script>",
                    'images' => ['description' => [['id' => 'shot', 'url' => 'https://img.example.com/shot.png']]],
                ],
                'releases' => [
                    $release('2.0.0-dev', 'dev', '2026-03-01T00:00:00Z'),
                    $release('1.0.0', 'stable', '2026-02-01T00:00:00Z'),
                ],
                'downloads' => ['total' => 99],
            ],
            [
                'id' => 'Loader',
                'published_at' => '2026-01-01T00:00:00Z',
                'updated_at' => '2026-01-01T00:00:00Z',
                'authored' => ['type' => 'mod-loader', 'name' => 'Loader', 'authors' => ['Val']],
                'releases' => [],
            ],
        ],
    ])]);
}

it('lists every mod from the content index', function () {
    fakeContentIndex();

    $this->get(route('mods.index'))->assertInertia(fn (Assert $page) => $page
        ->component('Mods/Index')
        ->has('mods', 2)
        ->where('mods.0.id', 'Rockets')
        ->where('mods.0.version', '1.0.0')
        ->where('mods.0.downloads', 99)
        ->where('mods.1.version', null)
        ->where('tagNames.gameplay', 'Gameplay')
        // What the filters need: the newest stable release's loader and game floor.
        ->where('mods.0.loader', 'StarMap')
        ->where('mods.0.game_min_revision', 5482)
        ->where('mods.1.loader', null)
        ->has('mods.0.updated_ago')
        // Newest first, and a month form has no revision to compare by.
        ->where('gameVersions', [
            ['label' => '2026.10.7.5541', 'revision' => 5541],
            ['label' => '2026.9.22.5482', 'revision' => 5482],
        ]));
});

it('shows a mod with resolved images and stripped html', function () {
    fakeContentIndex();

    $this->get(route('mods.show', 'Rockets'))->assertInertia(fn (Assert $page) => $page
        ->component('Mods/Show')
        ->where('mod.description', fn (string $html) => str_contains($html, 'src="https://img.example.com/shot.png"')
            && ! str_contains($html, '<script>'))
        ->where('mod.releases.0.version', '2.0.0-dev')
        ->where('mod.releases.0.dependencies.0', ['id' => 'Loader', 'name' => 'Loader', 'kind' => 'required', 'min' => '0.4', 'max' => null])
        ->where('mod.releases.0.dependencies.1.name', null));
});

it('shows a mod\'s gallery images and what its release needs from the game', function () {
    fakeContentIndex();

    $this->get(route('mods.show', 'Rockets'))->assertInertia(fn (Assert $page) => $page
        ->where('mod.images', ['https://img.example.com/shot.png'])
        ->where('mod.releases.1.game_min_revision', 5482)
        ->where('gameVersions.0', ['label' => '2026.10.7.5541', 'revision' => 5541])
        ->where('dependents', []));
});

it('lists the mods whose newest release requires a mod', function () {
    fakeContentIndex();

    $this->get(route('mods.show', 'Loader'))->assertInertia(fn (Assert $page) => $page
        ->where('dependents', [['id' => 'Rockets', 'name' => 'Rockets', 'icon' => null]]));
});

it('lists every public page and mod in the sitemap', function () {
    fakeContentIndex();

    $this->get('/sitemap.xml')
        ->assertOk()
        ->assertHeader('Content-Type', 'application/xml')
        ->assertSee('<loc>'.route('mods.show', 'Rockets').'</loc><lastmod>2026-03-01T00:00:00Z</lastmod>', false)
        ->assertSee('<loc>'.route('privacy').'</loc>', false);
});

it('returns 404 for an unknown mod', function () {
    fakeContentIndex();

    $this->get(route('mods.show', 'Nope'))->assertNotFound();
});

it('hands the page the browser\'s user agent, so a server-side render picks the same buttons', function () {
    fakeContentIndex();

    $this->withHeader('User-Agent', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)')
        ->get(route('mods.show', 'Rockets'))
        ->assertInertia(fn (Assert $page) => $page->where('userAgent', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'));
});

it('serves the cached index when the source is down', function () {
    fakeContentIndex();
    $this->get(route('home'))->assertOk();

    Http::fake([ContentIndex::URL => Http::response(status: 500)]);

    $this->get(route('home'))->assertOk();
});
