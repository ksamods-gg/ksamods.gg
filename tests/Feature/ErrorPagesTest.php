<?php

use App\Models\User;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Route;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();
    Http::fake([ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => []])]);
    Route::middleware('web')->get('/_test/boom', fn () => throw new RuntimeException('Boom'));
});

it('shows the site\'s own page, with its header, when a page is missing or forbidden', function () {
    $this->get('/no-such-page')->assertNotFound()
        ->assertInertia(fn (Assert $page) => $page->component('Error')->where('status', 404)->has('auth'));
    $this->get(route('mods.show', 'Nope'))->assertNotFound()
        ->assertInertia(fn (Assert $page) => $page->component('Error')->where('status', 404));

    $this->actingAs(User::factory()->create())->get(route('admin.dashboard'))->assertForbidden()
        ->assertInertia(fn (Assert $page) => $page->component('Error')->where('status', 403));
});

it('shows a server error without the shared data, which may be what failed', function () {
    config(['app.debug' => false]);

    $this->get('/_test/boom')->assertStatus(500)
        ->assertInertia(fn (Assert $page) => $page->component('Error')->where('status', 500)->missing('auth'));
});

it('keeps Laravel\'s error page for a server error while debugging', function () {
    config(['app.debug' => true]);

    $response = $this->get('/_test/boom')->assertStatus(500);
    expect($response->headers->get('X-Inertia'))->toBeNull()
        ->and($response->getContent())->not->toContain('"component":"Error"');
});

it('keeps JSON errors for the upload API and other JSON requests', function () {
    $this->postJson('/api/share/v1/uploads')->assertUnauthorized()->assertJson(['error' => 'unauthorized']);
    $this->getJson('/no-such-page')->assertNotFound()->assertJsonMissingPath('component');
});
