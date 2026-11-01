<?php

use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Laravel\Socialite\Facades\Socialite;
use Laravel\Socialite\Two\GithubProvider;
use Laravel\Socialite\Two\User as SocialiteUser;
use Symfony\Component\HttpFoundation\RedirectResponse;

uses(RefreshDatabase::class);

function fakeGitHubUser(string $name): void
{
    $githubUser = (new SocialiteUser)->map([
        'id' => '42',
        'nickname' => 'octocat',
        'name' => $name,
        'email' => 'octocat@example.com',
        'avatar' => 'https://example.com/a.png',
    ])->setToken('gho_secret')->setApprovedScopes(['public_repo', 'user:email']);

    Socialite::shouldReceive('driver')->andReturn(Mockery::mock(GithubProvider::class, ['user' => $githubUser]));
}

it('creates and logs in a user from github', function () {
    fakeGitHubUser('Octo Cat');

    $this->get('/auth/github/callback')->assertRedirect(route('home'));

    $this->assertAuthenticated();
    $user = User::sole();
    expect($user->github_id)->toBe('42')
        ->and($user->github_login)->toBe('octocat')
        ->and($user->github_token)->toBe('gho_secret')
        ->and($user->getRawOriginal('github_token'))->not->toBe('gho_secret')
        ->and($user->canOpenPullRequests())->toBeTrue();
});

it('never sends the github token to the browser', function () {
    $this->withoutVite();
    Http::fake(['*' => ['listings' => [], 'tags' => []]]);

    $this->actingAs(User::factory()->create(['github_token' => 'gho_secret']))
        ->get(route('home'))
        ->assertDontSee('gho_secret')
        ->assertInertia(fn ($page) => $page->missing('auth.user.github_token'));
});

it('updates the existing user on repeat login', function () {
    User::factory()->create(['github_id' => '42', 'name' => 'Old Name', 'github_token' => 'gho_old']);
    fakeGitHubUser('Octo Cat');

    $this->get('/auth/github/callback');

    // The display name is the GitHub username, not the profile name.
    expect(User::sole())
        ->name->toBe('octocat')
        ->github_token->toBe('gho_secret');
});

it('keeps the player signed in only when they ticked the box', function (bool $remember) {
    $provider = Mockery::mock(GithubProvider::class);
    $provider->shouldReceive('redirect')->andReturn(new RedirectResponse('https://github.com/login'));
    $provider->shouldReceive('user')->andReturn((new SocialiteUser)->map(['id' => '42', 'nickname' => 'octocat'])->setToken('gho_secret')->setApprovedScopes([]));
    Socialite::shouldReceive('driver')->andReturn($provider);

    // The login link carries the choice to the redirect, which keeps it for the callback.
    $this->get(route('github.login', $remember ? ['remember' => 1] : []))->assertSessionHas('auth.remember', $remember);
    $response = $this->withSession(['auth.remember' => $remember])->get('/auth/github/callback')->assertRedirect(route('home'));

    $cookies = collect($response->headers->getCookies())->map->getName();
    expect($cookies->contains(fn ($name) => str_starts_with($name, 'remember_web_')))->toBe($remember);
})->with([true, false]);

it('shares the analytics id only when one is configured', function (?string $id) {
    $this->withoutVite();
    Http::fake(['*' => ['listings' => [], 'tags' => []]]);
    config(['services.google_analytics.id' => $id]);

    $this->get(route('home'))->assertInertia(fn ($page) => $page->where('analytics.google', $id));
})->with(['G-TEST', null]);

it('logs out', function () {
    $this->actingAs(User::factory()->create())
        ->post('/logout')
        ->assertRedirect(route('home'));

    $this->assertGuest();
});
