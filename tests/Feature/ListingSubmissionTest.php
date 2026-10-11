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

    Http::fake([
        ContentIndex::URL => Http::response([
            'tags' => ['mod' => [['tag' => 'gameplay', 'name' => 'Gameplay']]],
            'game_versions' => ['versions' => ['2026.9.1.100', '2026.10.7.5541']],
            'listings' => [
                ['id' => 'StarMap', 'authored' => ['type' => 'mod-loader', 'name' => 'StarMap']],
                ['id' => 'Taken', 'authored' => ['type' => 'mod', 'name' => 'Taken']],
            ],
        ]),
        // Release repositories: one the user owns, one an organization owns.
        'api.github.com/repos/jeb/better-rockets' => Http::response(['owner' => ['id' => 1001], 'topics' => []]),
        'api.github.com/repos/rocket-org/better-rockets' => Http::response(['owner' => ['id' => 999], 'topics' => ['ksa', 'rockets']]),
        'api.github.com/repos/rocket-org/better-rockets/topics' => fn () => $this->topicsResponse ?? Http::response(['names' => []]),
        'api.github.com/repos/KSAModding/content-index/forks' => Http::response(['full_name' => 'jeb/content-index'], 202),
        'api.github.com/repos/jeb/content-index/merge-upstream' => Http::sequence()->push([], 404)->push(['message' => 'ok']),
        'api.github.com/repos/KSAModding/content-index/git/ref/heads/main' => Http::response(['object' => ['sha' => 'abc123']]),
        'api.github.com/repos/jeb/content-index/git/refs' => Http::response([], 201),
        'api.github.com/repos/jeb/content-index/contents/*' => Http::response([], 201),
        // The first matching fake wins, so a test swaps this response through $this->pullsResponse.
        'api.github.com/repos/KSAModding/content-index/pulls' => fn () => $this->pullsResponse
            ?? Http::response(['html_url' => 'https://github.com/KSAModding/content-index/pull/42'], 201),
    ]);
});

function publisher(): User
{
    return User::factory()->create([
        'github_id' => '1001',
        'github_login' => 'Jeb',
        'github_token' => 'gho_secret',
        'github_scopes' => 'public_repo,user:email',
    ]);
}

function validListing(array $overrides = []): array
{
    return [
        'id' => 'BetterRockets',
        'name' => 'Better "Rockets"',
        'authors' => 'Jeb, Bill',
        'abstract' => 'Rockets, but better.',
        'description' => "## Features\r\n\r\n- Faster",
        'license' => 'MIT',
        'tags' => ['gameplay'],
        'github' => 'jeb/better-rockets',
        'forums' => 'https://forums.ahwoo.com/threads/better-rockets.123/',
        'game_min' => '2026.10.7.5541',
        'loader_id' => 'StarMap',
        'loader_min' => '0.4.5',
        ...$overrides,
    ];
}

it('sends guests to log in', function () {
    $this->get(route('listings.create'))->assertRedirect(route('login'));
});

it('asks for pull request access before showing the form', function () {
    $user = User::factory()->create(['github_token' => 'gho_x', 'github_scopes' => 'user:email']);

    $this->actingAs($user)->get(route('listings.create'))->assertInertia(fn (Assert $page) => $page
        ->component('Listings/Form')
        ->where('canOpenPullRequests', false)
        ->where('gameVersions.0', '2026.10.7.5541')
        ->where('loaders', ['StarMap']));

    $this->actingAs($user)->post(route('listings.store'), validListing())->assertForbidden();
});

it('says so when GitHub comes back without the scope', function () {
    // What a GitHub App returns, since it ignores OAuth scopes.
    $user = User::factory()->create(['github_token' => 'ghu_x', 'github_scopes' => '']);

    $this->actingAs($user)
        ->withSession(['github.requested_pull_requests' => true])
        ->get(route('listings.create'))
        ->assertInertia(fn (Assert $page) => $page->where('connectFailed', true));

    $this->actingAs($user)
        ->get(route('listings.create'))
        ->assertInertia(fn (Assert $page) => $page->where('connectFailed', false));
});

it('opens a pull request from the user\'s fork', function () {
    $this->actingAs(publisher())
        ->post(route('listings.store'), validListing())
        ->assertRedirect('https://github.com/KSAModding/content-index/pull/42');

    Http::assertSent(function (Request $request) {
        if ($request->method() !== 'PUT') {
            return false;
        }

        $toml = base64_decode($request['content']);

        return str_ends_with($request->url(), '/contents/listings/BetterRockets.toml')
            && $request->hasHeader('Authorization', 'Bearer gho_secret')
            && str_starts_with($request['branch'], 'listing/BetterRockets-')
            && str_contains($toml, 'name = "Better \"Rockets\""')
            && str_contains($toml, 'authors = ["Jeb", "Bill"]')
            && str_contains($toml, "description = '''\n## Features\n\n- Faster\n'''")
            && str_contains($toml, "[releases]\ngithub = \"jeb/better-rockets\"\n")
            && str_contains($toml, "[loader]\nid = \"StarMap\"\nmin = \"0.4.5\"\n");
    });

    Http::assertSent(fn (Request $request) => str_ends_with($request->url(), '/git/refs') && $request['sha'] === 'abc123');
    Http::assertSent(fn (Request $request) => str_ends_with($request->url(), '/pulls')
        && str_starts_with($request['head'], 'jeb:listing/BetterRockets-')
        && $request['base'] === 'main');
});

it('needs no topic on a repository the user owns', function () {
    $this->actingAs(publisher())->post(route('listings.store'), validListing());

    Http::assertNotSent(fn (Request $request) => str_ends_with($request->url(), '/topics'));
});

it('adds the ownership topic to an organization repository, keeping its topics', function () {
    $this->actingAs(publisher())
        ->post(route('listings.store'), validListing(['github' => 'rocket-org/better-rockets']))
        ->assertRedirect('https://github.com/KSAModding/content-index/pull/42');

    Http::assertSent(fn (Request $request) => $request->method() === 'PUT'
        && str_ends_with($request->url(), '/repos/rocket-org/better-rockets/topics')
        && $request['names'] === ['ksa', 'rockets', 'ksa-index-jeb']);

    // The topic goes on before the pull request opens, so the index's check sees it.
    $urls = Http::recorded()->map(fn (array $pair) => $pair[0]->url())->values();
    expect($urls->search(fn (string $url) => str_ends_with($url, '/topics')))
        ->toBeLessThan($urls->search(fn (string $url) => str_ends_with($url, '/pulls')));
});

it('still opens the pull request when the topic cannot be added', function () {
    $this->topicsResponse = Http::response(['message' => 'Must have admin rights to Repository.'], 403);

    $this->actingAs(publisher())
        ->post(route('listings.store'), validListing(['github' => 'rocket-org/better-rockets']))
        ->assertRedirect('https://github.com/KSAModding/content-index/pull/42');
});

it('leaves the proof to the author when SpaceDock is the authority', function () {
    $this->actingAs(publisher())->post(route('listings.store'), validListing([
        'github' => 'rocket-org/better-rockets',
        'spacedock' => 12,
        'authority' => 'spacedock',
    ]));

    Http::assertNotSent(fn (Request $request) => str_contains($request->url(), 'rocket-org'));
});

it('rejects listings the index would refuse', function (array $overrides, string $field) {
    $this->actingAs(publisher())
        ->post(route('listings.store'), validListing($overrides))
        ->assertSessionHasErrors($field);

    Http::assertNotSent(fn (Request $request) => str_contains($request->url(), 'api.github.com'));
})->with([
    'a taken id, in any case' => [['id' => 'taken'], 'id'],
    'a reserved id' => [['id' => 'CON'], 'id'],
    'a forum link off Ahwoo' => [['forums' => 'https://example.com/thread'], 'forums'],
    'no release host' => [['github' => null], 'github'],
    'two hosts without authority' => [['spacedock' => 12], 'authority'],
    'an unknown game version' => [['game_min' => '2020.1.1.1'], 'game_min'],
    'a loader without a version' => [['loader_min' => null], 'loader_min'],
    'a license that is not SPDX' => [['license' => 'MIT, Apache-2.0'], 'license'],
]);

it('reports GitHub refusing the pull request', function () {
    $this->pullsResponse = Http::response(['message' => 'Validation Failed'], 422);

    $this->actingAs(publisher())
        ->from(route('listings.create'))
        ->post(route('listings.store'), validListing())
        ->assertRedirect(route('listings.create'))
        ->assertSessionHasErrors(['pull_request' => 'GitHub refused the pull request: Validation Failed. If this keeps happening, reconnect GitHub and try again.']);
});
