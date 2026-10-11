<?php

use App\Models\User;
use App\Support\ContentIndex;
use Devium\Toml\Toml;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\Client\Request;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Sleep;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

// A listing with fields the form does not manage, which an edit must keep.
const MEASURE_TOOLS = <<<'TOML'
spec_version = 1
id = "MeasureTools"
type = "mod"
name = "MeasureTools"
authors = ["Maxi"]
abstract = "Click-to-measure tools."
description = '''
## Features

- Ruler
'''
license = "MIT"
tags = ["user-interface", "measuring"]

[releases]
github = "Maxi/KSA-MeasureTools"
since = "1.0"

[links]
forums = "https://forums.ahwoo.com/threads/measuretools.992/"
repository = "https://github.com/Maxi/KSA-MeasureTools"
spacedock = "https://spacedock.info/mod/4319/MeasureTools"

[compatibility]
game_min = "2026.7"
game_max = "2026.12"

[loader]
id = "StarMap"
min = "0.4.5"
max = "0.9"

[images.icon]
url = "https://example.com/icon.png"
sha256 = "87adc77a14635e7fa40628f8222ab9db57e00554c3542ba1f955fff9b1e51a0f"
width = 512
height = 512
size = 132027

[[images.description]]
id = "map-view"
url = "https://example.com/map.png"
sha256 = "c42c1007e6dd9238bc17247d1a1b799b0bc285ac251c9473f6b9e4a11a337191"
width = 2048
height = 1163
size = 901551

[[dependencies]]
id = "ModMenu"
kind = "optional"
TOML;

const STARMAP = <<<'TOML'
spec_version = 1
id = "StarMap"
type = "mod-loader"
name = "StarMap"
authors = ["KlaasWhite"]
abstract = "Mod loader."
license = "MIT"

[releases]
github = "StarMapLoader/StarMap"

[links]
forums = "https://forums.ahwoo.com/threads/starmap.1/"

[compatibility]
game_min = "2026.10.7.5541"

[install]
target = "root"

[provides]
launch = "StarMap.exe"
TOML;

beforeEach(function () {
    $this->withoutVite();
    Sleep::fake();

    $files = ['MeasureTools' => MEASURE_TOOLS, 'StarMap' => STARMAP];

    Http::fake([
        ContentIndex::URL => Http::response([
            'tags' => ['mod' => [['tag' => 'user-interface', 'name' => 'User Interface']]],
            'game_versions' => ['versions' => ['2026.10.7.5541']],
            'listings' => [
                ['id' => 'StarMap', 'authored' => ['type' => 'mod-loader', 'name' => 'StarMap']],
                ['id' => 'MeasureTools', 'authored' => ['type' => 'mod', 'name' => 'MeasureTools']],
            ],
        ]),
        'api.github.com/repos/KSAModding/content-index/contents/listings/*' => function (Request $request) use ($files) {
            $id = urldecode(basename(parse_url($request->url(), PHP_URL_PATH), '.toml'));

            return isset($files[$id])
                ? Http::response(['content' => base64_encode($files[$id]), 'sha' => "blob-{$id}"])
                : Http::response(['message' => 'Not Found'], 404);
        },
        'api.github.com/repos/Maxi/KSA-MeasureTools' => Http::response(['owner' => ['id' => 1001], 'topics' => []]),
        'api.github.com/repos/StarMapLoader/StarMap' => Http::response(['owner' => ['id' => 1001], 'topics' => []]),
        'api.github.com/repos/KSAModding/content-index/forks' => Http::response(['full_name' => 'maxi/content-index'], 202),
        'api.github.com/repos/maxi/content-index/merge-upstream' => Http::response(['message' => 'ok']),
        'api.github.com/repos/KSAModding/content-index/git/ref/heads/main' => Http::response(['object' => ['sha' => 'abc123']]),
        'api.github.com/repos/maxi/content-index/git/refs' => Http::response([], 201),
        'api.github.com/repos/maxi/content-index/contents/*' => Http::response([], 200),
        'api.github.com/repos/KSAModding/content-index/pulls' => Http::response(['html_url' => 'https://github.com/KSAModding/content-index/pull/7'], 201),
    ]);

    $this->user = User::factory()->create([
        'github_id' => '1001',
        'github_login' => 'maxi',
        'github_token' => 'gho_secret',
        'github_scopes' => 'public_repo',
    ]);
});

/**
 * The form's values for MeasureTools, unchanged.
 */
function measureToolsForm(array $overrides = []): array
{
    return [
        'name' => 'MeasureTools',
        'authors' => 'Maxi',
        'abstract' => 'Click-to-measure tools.',
        'description' => "## Features\n\n- Ruler",
        'license' => 'MIT',
        'tags' => ['user-interface', 'measuring'],
        'github' => 'Maxi/KSA-MeasureTools',
        'forums' => 'https://forums.ahwoo.com/threads/measuretools.992/',
        'repository' => 'https://github.com/Maxi/KSA-MeasureTools',
        'game_min' => '2026.7',
        'loader_id' => 'StarMap',
        'loader_min' => '0.4.5',
        ...$overrides,
    ];
}

/**
 * The listing file the pull request commits, decoded.
 */
function committedListing(): array
{
    $put = Http::recorded(fn (Request $request) => $request->method() === 'PUT' && str_contains($request->url(), 'maxi/content-index/contents'))->sole()[0];

    return Toml::decode(base64_decode($put['content']), asArray: true);
}

it('fills the form from the listing on main', function () {
    $this->actingAs($this->user)->get(route('listings.edit', 'MeasureTools'))->assertInertia(fn (Assert $page) => $page
        ->component('Listings/Form')
        ->where('listing.id', 'MeasureTools')
        ->where('listing.authors', 'Maxi')
        ->where('listing.description', "## Features\n\n- Ruler")
        ->where('listing.game_min', '2026.7')
        ->where('listing.loader_min', '0.4.5')
        // Values outside the index's lists stay selectable, so an unchanged form is valid.
        ->where('tags.1', ['tag' => 'measuring', 'name' => 'measuring'])
        ->where('gameVersions', ['2026.10.7.5541', '2026.7']));
});

it('returns 404 for a listing that is not in the index', function () {
    $this->actingAs($this->user)->get(route('listings.edit', 'Nope'))->assertNotFound();
});

it('changes only the fields the form manages', function () {
    $this->actingAs($this->user)
        ->put(route('listings.update', 'MeasureTools'), measureToolsForm([
            'abstract' => 'Rulers and protractors.',
            'repository' => null,
        ]))
        ->assertRedirect('https://github.com/KSAModding/content-index/pull/7');

    $expected = Toml::decode(MEASURE_TOOLS, asArray: true);
    $expected['abstract'] = 'Rulers and protractors.';
    unset($expected['links']['repository']);

    expect(committedListing())->toEqual($expected);

    Http::assertSent(fn (Request $request) => $request->method() === 'PUT'
        && str_contains($request->url(), 'maxi/content-index/contents/listings/MeasureTools.toml')
        && $request['sha'] === 'blob-MeasureTools'
        && $request['message'] === 'Update MeasureTools');
    Http::assertSent(fn (Request $request) => str_ends_with($request->url(), '/pulls') && $request['title'] === 'Update MeasureTools');
});

it('drops the loader bounds with the loader', function () {
    $this->actingAs($this->user)->put(route('listings.update', 'MeasureTools'), measureToolsForm(['loader_id' => null, 'loader_min' => null]));

    expect(committedListing())->not->toHaveKey('loader');
});

it('keeps a mod loader free of a [loader] section', function () {
    $this->actingAs($this->user)->put(route('listings.update', 'StarMap'), [
        'name' => 'StarMap',
        'authors' => 'KlaasWhite',
        'abstract' => 'The mod loader.',
        'license' => 'MIT',
        'github' => 'StarMapLoader/StarMap',
        'forums' => 'https://forums.ahwoo.com/threads/starmap.1/',
        'game_min' => '2026.10.7.5541',
        'loader_id' => 'StarMap',
        'loader_min' => '1.0',
    ]);

    expect(committedListing())
        ->not->toHaveKey('loader')
        ->abstract->toBe('The mod loader.')
        ->provides->toBe(['launch' => 'StarMap.exe']);
});

it('cannot rename a listing', function () {
    $this->actingAs($this->user)->put(route('listings.update', 'MeasureTools'), measureToolsForm(['id' => 'Renamed']));

    expect(committedListing()['id'])->toBe('MeasureTools');
});
