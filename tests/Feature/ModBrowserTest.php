<?php

use App\Support\ContentIndex;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;

beforeEach(fn () => $this->withoutVite());

function fakeContentIndex(): void
{
    $release = fn (string $version, string $status, string $date) => [
        'version' => $version,
        'release_status' => $status,
        'release_date' => $date,
        'download' => ['url' => "https://example.com/{$version}.zip", 'size' => 1024],
        'dependencies' => [['id' => 'Loader', 'kind' => 'required'], ['id' => 'Unlisted', 'kind' => 'optional']],
        'changelog_text' => '## Changes',
    ];

    Http::fake([ContentIndex::URL => Http::response([
        'tags' => ['mod' => [['tag' => 'gameplay', 'name' => 'Gameplay']]],
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

    $this->get(route('home'))->assertInertia(fn (Assert $page) => $page
        ->component('Mods/Index')
        ->has('mods', 2)
        ->where('mods.0.id', 'Rockets')
        ->where('mods.0.version', '1.0.0')
        ->where('mods.0.downloads', 99)
        ->where('mods.1.version', null)
        ->where('tagNames.gameplay', 'Gameplay'));
});

it('shows a mod with resolved images and stripped html', function () {
    fakeContentIndex();

    $this->get(route('mods.show', 'Rockets'))->assertInertia(fn (Assert $page) => $page
        ->component('Mods/Show')
        ->where('mod.description', fn (string $html) => str_contains($html, 'src="https://img.example.com/shot.png"')
            && ! str_contains($html, '<script>'))
        ->where('mod.releases.0.version', '2.0.0-dev')
        ->where('mod.releases.0.dependencies.0.listed', true)
        ->where('mod.releases.0.dependencies.1.listed', false));
});

it('returns 404 for an unknown mod', function () {
    fakeContentIndex();

    $this->get(route('mods.show', 'Nope'))->assertNotFound();
});

it('serves the cached index when the source is down', function () {
    fakeContentIndex();
    $this->get(route('home'))->assertOk();

    Http::fake([ContentIndex::URL => Http::response(status: 500)]);

    $this->get(route('home'))->assertOk();
});
