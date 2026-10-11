<?php

use App\Models\User;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\Client\Request;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

function listingOn(string $id, array $releases): array
{
    return [
        'id' => $id,
        'updated_at' => '2026-10-01T00:00:00Z',
        'authored' => ['type' => 'mod', 'name' => $id, 'releases' => $releases, 'links' => []],
        'releases' => [],
    ];
}

beforeEach(function () {
    $this->withoutVite();

    $repository = fn (int $owner, array $extra = []) => Http::response(['owner' => ['id' => $owner], 'topics' => [], 'fork' => false, 'permissions' => ['admin' => false], ...$extra]);

    Http::fake([
        ContentIndex::URL => Http::response([
            'tags' => ['mod' => []],
            'listings' => [
                listingOn('Owned', ['github' => 'jeb/owned']),
                listingOn('Admin', ['github' => 'org/admin']),
                listingOn('Topic', ['github' => 'org/topic']),
                listingOn('Marker', ['github' => 'org/marker']),
                listingOn('Stranger', ['github' => 'org/stranger']),
                listingOn('ViaSpaceDock', ['spacedock' => 77]),
                listingOn('NoRepository', []),
            ],
        ]),
        'spacedock.info/api/mod/77' => Http::response(['id' => 77, 'source_code' => 'https://github.com/jeb/spacedock-mod']),
        'api.github.com/repos/jeb/owned' => $repository(1001),
        'api.github.com/repos/jeb/spacedock-mod' => $repository(1001),
        'api.github.com/repos/org/admin' => $repository(9, ['permissions' => ['admin' => true]]),
        'api.github.com/repos/org/topic' => $repository(9, ['topics' => ['ksa-index-jeb']]),
        'api.github.com/repos/org/marker' => $repository(9),
        'api.github.com/repos/org/marker/contents/*' => Http::response(['content' => base64_encode("login = \"Jeb\"\n")]),
        'api.github.com/repos/org/stranger' => $repository(9),
        'api.github.com/repos/org/stranger/contents/*' => Http::response([], 404),
    ]);

    $this->user = User::factory()->create(['github_id' => '1001', 'github_login' => 'Jeb', 'github_token' => 'gho_x']);
});

it('lists the mods the user owns or can manage, and why', function () {
    $this->actingAs($this->user)->get(route('mods.mine'))->assertInertia(fn (Assert $page) => $page
        ->component('Mods/Mine')
        ->where('mods', fn ($mods) => collect($mods)->pluck('role', 'id')->all() === [
            'Owned' => 'owner',
            'Admin' => 'admin',
            'Topic' => 'topic',
            'Marker' => 'marker',
            'ViaSpaceDock' => 'owner',
        ]));
});

it('asks GitHub once per listing, all at once, and then remembers', function () {
    $this->actingAs($this->user)->get(route('mods.mine'))->assertOk();
    $this->actingAs($this->user)->get(route('mods.mine'))->assertOk();

    // Six repositories and two marker files, from the first visit only.
    expect(Http::recorded(fn (Request $request) => str_contains($request->url(), 'api.github.com')))->toHaveCount(8);
});

it('sends guests to log in', function () {
    $this->get(route('mods.mine'))->assertRedirect(route('login'));
});
