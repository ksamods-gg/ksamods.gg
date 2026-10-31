<?php

use App\Models\Review;
use App\Models\User;
use App\Support\ContentIndex;
use App\Support\ModSocial;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();
    Http::fake([ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => [
        [
            'id' => 'Rockets', 'published_at' => '2026-01-01T00:00:00Z', 'updated_at' => '2026-01-02T00:00:00Z',
            'authored' => ['type' => 'mod', 'name' => 'Rockets', 'releases' => ['github' => 'Pilot/rockets']],
            'releases' => [],
        ],
        [
            'id' => 'Wheels', 'published_at' => '2026-01-01T00:00:00Z', 'updated_at' => '2026-01-02T00:00:00Z',
            'authored' => ['type' => 'mod', 'name' => 'Wheels', 'releases' => ['github' => 'someone-else/wheels']],
            'releases' => [],
        ],
    ]])]);
});

it('shows nothing until the player turns their profile on', function () {
    User::factory()->create(['username' => 'pilot']);

    $this->get('/u/pilot')->assertNotFound();
});

it('shows a profile with the sections the player leaves visible', function () {
    $user = User::factory()->create([
        'username' => 'pilot', 'profile_enabled' => true, 'github_login' => 'Pilot', 'bio' => 'I fly.',
        'profile_sections' => ['comments' => false],
    ]);
    (new Review)->forceFill(['user_id' => $user->id, 'listing_id' => 'Wheels', 'rating' => 4, 'body' => 'Good wheels.'])->save();

    $this->get('/u/PILOT')->assertOk()->assertInertia(fn (Assert $page) => $page
        ->component('Profiles/Show')
        ->where('profile.username', 'pilot')
        ->where('profile.bio', 'I fly.')
        // GitHub stays hidden until the player chooses to show it.
        ->where('profile.github', null)
        // Their own repository's mod, not someone else's.
        ->has('mods', 1)
        ->where('mods.0.id', 'Rockets')
        ->where('reviews.0.mod.name', 'Wheels')
        ->where('comments', null));
});

it('saves the profile settings, with a free and valid username', function () {
    User::factory()->create(['username' => 'taken']);
    $user = User::factory()->create();

    $this->actingAs($user)->put('/settings/profile', ['enabled' => true, 'username' => 'Admin'])->assertSessionHasErrors('username');
    $this->actingAs($user)->put('/settings/profile', ['enabled' => true, 'username' => 'TAKEN'])->assertSessionHasErrors('username');
    $this->actingAs($user)->put('/settings/profile', ['enabled' => true, 'username' => 'a'])->assertSessionHasErrors('username');
    $this->actingAs($user)->put('/settings/profile', ['enabled' => true])->assertSessionHasErrors('username');

    $this->actingAs($user)->put('/settings/profile', [
        'enabled' => true, 'username' => 'Rocket_Kitten', 'bio' => 'Hi.', 'links' => ['https://example.com', '', '', ''],
        'showAccounts' => true, 'sections' => ['reviews' => false],
    ])->assertSessionHasNoErrors();

    expect($user->fresh())
        ->username->toBe('rocket_kitten')
        ->profile_links->toBe(['https://example.com'])
        ->profileUrl()->toBe(route('profiles.show', 'rocket_kitten'))
        ->showsOnProfile('reviews')->toBeFalse()
        ->showsOnProfile('mods')->toBeTrue();
});

it('links an author on a mod page to their profile only while it is on', function () {
    $on = User::factory()->create(['username' => 'on', 'profile_enabled' => true]);
    $off = User::factory()->create(['username' => 'off']);
    (new Review)->forceFill(['user_id' => $on->id, 'listing_id' => 'Rockets', 'rating' => 5, 'body' => 'A'])->save();
    (new Review)->forceFill(['user_id' => $off->id, 'listing_id' => 'Rockets', 'rating' => 3, 'body' => 'B'])->save();

    $profiles = collect(ModSocial::forListing('Rockets', null)['reviews']['items'])->pluck('author.profile', 'body');

    expect($profiles->all())->toEqualCanonicalizing(['A' => route('profiles.show', 'on'), 'B' => null])
        ->and($profiles['B'])->toBeNull();
});
