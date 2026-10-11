<?php

use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Laravel\Socialite\Facades\Socialite;
use Laravel\Socialite\Two\User as SocialiteUser;

uses(RefreshDatabase::class);

function fakeGitHubUser(string $name): void
{
    $githubUser = (new SocialiteUser)->map([
        'id' => '42',
        'nickname' => 'octocat',
        'name' => $name,
        'email' => 'octocat@example.com',
        'avatar' => 'https://example.com/a.png',
    ]);

    Socialite::shouldReceive('driver->user')->andReturn($githubUser);
}

it('creates and logs in a user from github', function () {
    fakeGitHubUser('Octo Cat');

    $this->get('/auth/github/callback')->assertRedirect(route('home'));

    $this->assertAuthenticated();
    expect(User::sole()->github_id)->toBe('42');
});

it('updates the existing user on repeat login', function () {
    User::factory()->create(['github_id' => '42', 'name' => 'Old Name']);
    fakeGitHubUser('Renamed');

    $this->get('/auth/github/callback');

    expect(User::sole()->name)->toBe('Renamed');
});

it('logs out', function () {
    $this->actingAs(User::factory()->create())
        ->post('/logout')
        ->assertRedirect(route('home'));

    $this->assertGuest();
});
