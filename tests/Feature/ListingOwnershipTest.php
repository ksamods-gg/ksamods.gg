<?php

use App\Models\User;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\Client\Request;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Sleep;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();
    Sleep::fake();

    // What GitHub says about the release repository; each test sets what it needs.
    $this->repository = ['owner' => ['id' => 555], 'topics' => [], 'fork' => false, 'permissions' => ['admin' => false]];
    $this->marker = null;
    $this->releases = "[releases]\ngithub = \"rocket-org/rockets\"\n";

    Http::fake([
        ContentIndex::URL => Http::response([
            'tags' => ['mod' => []],
            'game_versions' => ['versions' => ['2026.10.7.5541']],
            'listings' => [['id' => 'Rockets', 'authored' => ['type' => 'mod', 'name' => 'Rockets']]],
        ]),
        'api.github.com/repos/KSAModding/content-index/contents/listings/*' => fn () => Http::response(['sha' => 'blob', 'content' => base64_encode(
            "spec_version = 1\nid = \"Rockets\"\ntype = \"mod\"\nname = \"Rockets\"\nauthors = [\"Jeb\"]\nabstract = \"Rockets.\"\nlicense = \"MIT\"\n\n"
            .$this->releases
            ."\n[links]\nforums = \"https://forums.ahwoo.com/threads/rockets.1/\"\n\n[compatibility]\ngame_min = \"2026.10.7.5541\"\n"
        )]),
        'spacedock.info/api/mod/77' => Http::response(['id' => 77, 'source_code' => 'https://github.com/rocket-org/rockets.git']),
        'api.github.com/repos/rocket-org/rockets/contents/.github/ksa-content-index.toml' => fn () => $this->marker === null
            ? Http::response(['message' => 'Not Found'], 404)
            : Http::response(['content' => base64_encode($this->marker)]),
        'api.github.com/repos/rocket-org/rockets' => fn () => Http::response($this->repository),
        'api.github.com/repos/rocket-org/rockets/topics' => Http::response([]),
        'api.github.com/repos/KSAModding/content-index/forks' => Http::response(['full_name' => 'jeb/content-index'], 202),
        'api.github.com/repos/jeb/content-index/*' => Http::response([], 201),
        'api.github.com/repos/KSAModding/content-index/git/ref/heads/main' => Http::response(['object' => ['sha' => 'abc123']]),
        'api.github.com/repos/KSAModding/content-index/pulls' => Http::response(['html_url' => 'https://github.com/KSAModding/content-index/pull/9'], 201),
    ]);

    $this->user = User::factory()->create([
        'github_id' => '1001',
        'github_login' => 'Jeb',
        'github_token' => 'gho_secret',
        'github_scopes' => 'public_repo',
    ]);
});

function rocketsForm(): array
{
    return [
        'name' => 'Rockets',
        'authors' => 'Jeb',
        'abstract' => 'Faster rockets.',
        'license' => 'MIT',
        'github' => 'rocket-org/rockets',
        'forums' => 'https://forums.ahwoo.com/threads/rockets.1/',
        'game_min' => '2026.10.7.5541',
    ];
}

it('lets an owner edit', function (Closure $setUp) {
    $setUp->call($this);

    $this->actingAs($this->user)->get(route('listings.edit', 'Rockets'))
        ->assertInertia(fn (Assert $page) => $page->component('Listings/Form'));

    $this->actingAs($this->user)->put(route('listings.update', 'Rockets'), rocketsForm())
        ->assertRedirect('https://github.com/KSAModding/content-index/pull/9');
})->with([
    'who owns the repository' => [fn () => $this->repository['owner']['id'] = 1001],
    'an admin of it' => [fn () => $this->repository['permissions']['admin'] = true],
    'named by its topic' => [fn () => $this->repository['topics'] = ['ksa-index-jeb']],
    'named by its marker file' => [fn () => $this->marker = "login = \"jeb\"\n"],
    'named by a marker file for this listing' => [fn () => $this->marker = "login = \"Jeb\"\nid = \"rockets\"\n"],
]);

it('turns everyone else away', function (Closure $setUp) {
    $setUp->call($this);

    $this->actingAs($this->user)->get(route('listings.edit', 'Rockets'))
        ->assertInertia(fn (Assert $page) => $page
            ->component('Listings/NotOwner')
            ->where('repository', 'rocket-org/rockets'));

    $this->actingAs($this->user)->put(route('listings.update', 'Rockets'), rocketsForm())->assertForbidden();

    Http::assertNotSent(fn (Request $request) => str_contains($request->url(), '/forks') || str_contains($request->url(), '/topics'));
})->with([
    'with no proof' => [fn () => null],
    'with a topic for someone else' => [fn () => $this->repository['topics'] = ['ksa-index-bill']],
    'with a marker for another listing' => [fn () => $this->marker = "login = \"jeb\"\nid = \"OtherMod\"\n"],
    'with a marker on a fork, which inherits it' => [function () {
        $this->repository['fork'] = true;
        $this->marker = "login = \"jeb\"\n";
    }],
]);

it('checks the listing as it stands, not the repository the form names', function () {
    // The user owns jeb/rockets, but the listing's release host is rocket-org/rockets.
    $this->actingAs($this->user)
        ->put(route('listings.update', 'Rockets'), [...rocketsForm(), 'github' => 'jeb/rockets'])
        ->assertForbidden();
});

it('follows a SpaceDock mod to the repository it links as source code', function () {
    $this->releases = "[releases]\nspacedock = 77\n";
    $this->repository['topics'] = ['ksa-index-jeb'];

    $this->actingAs($this->user)->get(route('listings.edit', 'Rockets'))
        ->assertInertia(fn (Assert $page) => $page->component('Listings/Form'));
});

it('lets nobody edit a listing that names no GitHub repository', function () {
    $this->releases = '';

    $this->actingAs($this->user)->get(route('listings.edit', 'Rockets'))
        ->assertInertia(fn (Assert $page) => $page
            ->component('Listings/NotOwner')
            ->where('repository', null));
});
