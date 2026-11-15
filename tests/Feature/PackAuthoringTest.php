<?php

use App\Models\User;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\Client\Request;
use Illuminate\Http\UploadedFile;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Storage;
use Illuminate\Support\Sleep;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();
    Sleep::fake();
    Storage::fake(config('share.disk'));

    $release = fn (string $version, array $dependencies = []) => ['version' => $version, 'release_status' => 'stable', 'release_date' => '2026-10-01T00:00:00Z', 'download' => ['url' => 'https://example.com/x.zip'], 'dependencies' => $dependencies, 'game_min' => '2026.10.25.5707', 'game_min_revision' => 5707];
    $listing = fn (string $id, array $releases) => ['id' => $id, 'published_at' => '2026-01-01T00:00:00Z', 'updated_at' => '2026-01-01T00:00:00Z', 'authored' => ['type' => 'mod', 'name' => $id], 'releases' => $releases];

    Http::fake([
        ContentIndex::URL => Http::response([
            'tags' => ['mod' => [['tag' => 'parts', 'name' => 'Parts']]],
            'game_versions' => ['versions' => ['2026.10.25.5707', '2026.11.10.5801']],
            'listings' => [
                // Alpha needs Beta 1.0.0 or newer.
                $listing('Alpha', [$release('1.0.0', [['id' => 'Beta', 'kind' => 'required', 'min' => '1.0.0']])]),
                $listing('Beta', [$release('1.1.0'), $release('1.0.0')]),
            ],
            'packs' => [['id' => 'old-pack', 'updated_at' => '2026-10-01T00:00:00Z', 'versions' => [['authored' => [
                'id' => 'old-pack', 'type' => 'modpack', 'name' => 'Old Pack', 'authors' => ['Jeb'], 'version' => '1.2.0', 'abstract' => 'Old.',
                'license' => 'MIT', 'links' => ['forums' => 'https://forums.ahwoo.com/threads/old-pack.9/'],
                'images' => ['icon' => ['url' => 'https://example.com/icon.webp', 'sha256' => str_repeat('a', 64), 'width' => 512, 'height' => 512, 'size' => 100]],
                'mods' => [['id' => 'Beta', 'version' => '1.0.0']],
            ]]]]],
        ]),
        'api.github.com/repos/KSAModding/content-index/contents/packs/old-pack/owner.json*' => Http::response(['content' => base64_encode(json_encode(['github_login' => 'Jeb', 'github_id' => 1001]))]),
        'api.github.com/repos/KSAModding/content-index/forks' => Http::response(['full_name' => 'jeb/content-index'], 202),
        'api.github.com/repos/jeb/content-index/merge-upstream' => Http::response(['message' => 'ok']),
        'api.github.com/repos/KSAModding/content-index/git/ref/heads/main' => Http::response(['object' => ['sha' => 'abc123']]),
        'api.github.com/repos/jeb/content-index/git/refs' => Http::response([], 201),
        'api.github.com/repos/KSAModding/content-index/contents/*' => Http::response([], 404),
        'api.github.com/repos/jeb/content-index/contents/*' => Http::response([], 201),
        'api.github.com/repos/KSAModding/content-index/pulls' => Http::response(['html_url' => 'https://github.com/KSAModding/content-index/pull/77'], 201),
    ]);
});

function packAuthor(array $attributes = []): User
{
    return User::factory()->create(['github_id' => '1001', 'github_login' => 'Jeb', 'github_token' => 'gho_secret', 'github_scopes' => 'public_repo', ...$attributes]);
}

function packForm(array $overrides = []): array
{
    return [
        'id' => 'new-pack', 'version' => '1.0.0', 'name' => 'New Pack', 'authors' => 'Jeb, Bill', 'abstract' => 'A new pack.',
        'license' => 'MIT', 'forums' => 'https://forums.ahwoo.com/threads/new-pack.10/', 'game_min' => '2026.10.25.5707',
        'mods' => [['id' => 'Alpha', 'version' => '1.0.0'], ['id' => 'Beta', 'version' => '1.1.0']],
        ...$overrides,
    ];
}

it('adds the dependencies a pin needs, at the newest stable release inside its bounds', function () {
    $this->actingAs(packAuthor())
        ->getJson(route('packs.check', ['mods' => [['id' => 'Alpha', 'version' => '1.0.0']]]))
        ->assertOk()
        ->assertJson(['errors' => [], 'adds' => [['id' => 'Beta', 'version' => '1.1.0']], 'game_min' => '2026.10.25.5707']);
});

it('opens a pull request with the pack version and its owner record', function () {
    $this->actingAs(packAuthor())
        ->post(route('packs.store'), packForm(['icon' => UploadedFile::fake()->image('icon.png', 400, 300)]))
        ->assertRedirect('https://github.com/KSAModding/content-index/pull/77');

    $put = fn (string $path) => Http::recorded(fn (Request $request) => $request->method() === 'PUT' && str_ends_with($request->url(), $path))->first()[0] ?? null;
    $toml = base64_decode($put('/contents/packs/new-pack/1.0.0.toml')['content']);
    $owner = json_decode(base64_decode($put('/contents/packs/new-pack/owner.json')['content']), true);

    expect($toml)->toContain("type = \"modpack\"\n")
        ->toContain('authors = ["Jeb", "Bill"]')
        ->toContain("[[mods]]\nid = \"Alpha\"\nversion = \"1.0.0\"\n")
        ->toContain("[images.icon]\nurl = \"")
        ->toContain('width = 512')
        ->and($owner)->toBe(['github_login' => 'Jeb', 'github_id' => 1001]);
    Http::assertSent(fn (Request $request) => str_ends_with($request->url(), '/pulls') && str_starts_with($request['head'], 'jeb:pack/new-pack-'));
});

it('refuses a pack that misses a required dependency, or takes an id that is held', function () {
    $this->actingAs($author = packAuthor())
        ->post(route('packs.store'), packForm(['mods' => [['id' => 'Alpha', 'version' => '1.0.0']]]))
        ->assertSessionHasErrors(['mods' => "mods[0]: 'Alpha' 1.0.0 requires 'Beta' 1.0.0 or newer, and the pack does not pin it"]);

    $this->actingAs($author)->post(route('packs.store'), packForm(['id' => 'ALPHA']))->assertSessionHasErrors('id');
    $this->actingAs($author)->post(route('packs.store'), packForm(['id' => 'Old-Pack']))->assertSessionHasErrors('id');
    Http::assertNotSent(fn (Request $request) => str_ends_with($request->url(), '/pulls'));
});

it('lets only the pack\'s owner publish its next version, higher than the newest', function () {
    $this->actingAs(packAuthor(['github_id' => '2002']))
        ->get(route('packs.new-version', 'old-pack'))
        ->assertRedirect(route('packs.show', 'old-pack'));

    $this->actingAs($owner = packAuthor())
        ->get(route('packs.new-version', 'old-pack'))
        ->assertInertia(fn (Assert $page) => $page->component('Packs/Form')->where('pack.version', '1.2.1')->where('pack.previous', '1.2.0'));

    $this->actingAs($owner)->post(route('packs.store'), packForm(['of' => 'old-pack', 'version' => '1.2.0']))->assertSessionHasErrors('version');

    $this->actingAs($owner)->post(route('packs.store'), packForm(['of' => 'old-pack', 'version' => '1.3.0']))->assertRedirect();
    $toml = base64_decode(Http::recorded(fn (Request $request) => $request->method() === 'PUT' && str_ends_with($request->url(), '/contents/packs/old-pack/1.3.0.toml'))->first()[0]['content']);

    // The id stays the pack's, and without a new upload the icon stays as it was.
    expect($toml)->toContain('id = "old-pack"')->toContain('url = "https://example.com/icon.webp"');
    Http::assertNotSent(fn (Request $request) => $request->method() === 'PUT' && str_ends_with($request->url(), '/contents/packs/old-pack/owner.json'));
});
