<?php

use App\Models\User;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\Client\Request;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;
use Laravel\Socialite\Facades\Socialite;
use Laravel\Socialite\Two\GithubProvider;
use Laravel\Socialite\Two\User as GitHubUser;
use SocialiteProviders\Discord\Provider as DiscordProvider;
use SocialiteProviders\Manager\OAuth2\User as DiscordUser;

uses(RefreshDatabase::class);

beforeEach(fn () => $this->withoutVite());

/**
 * Discord answers with this account; only the identify scope may be asked for.
 */
function fakeDiscordAccount(string $id = '80351110224678912', ?string $globalName = 'Jebediah'): void
{
    $account = (new DiscordUser)
        ->setRaw(['id' => $id, 'username' => 'jeb_', 'global_name' => $globalName])
        ->map(['id' => $id, 'nickname' => 'jeb_', 'name' => 'jeb_', 'avatar' => 'https://cdn.discordapp.com/a.png']);

    $provider = Mockery::mock(DiscordProvider::class);
    $provider->shouldReceive('setScopes')->with(['identify'])->andReturnSelf();
    $provider->shouldReceive('user')->andReturn($account);
    $provider->shouldReceive('redirect')->andReturn(redirect('https://discord.com/oauth2/authorize'));

    Socialite::shouldReceive('driver')->with('discord')->andReturn($provider);
}

function fakeGitHubAccount(string $id = '42', string $login = 'jeb-gh'): void
{
    $account = (new GitHubUser)
        ->map(['id' => $id, 'nickname' => $login, 'name' => 'Jeb', 'email' => 'jeb@example.com', 'avatar' => 'https://github.com/a.png'])
        ->setToken('gho_secret')
        ->setApprovedScopes(['user:email']);

    Socialite::shouldReceive('driver')->with('github')->andReturn(Mockery::mock(GithubProvider::class, ['user' => $account]));
}

describe('Discord sign-in', function () {
    it('creates an account from Discord, with its display name and no email', function () {
        fakeDiscordAccount();

        $this->get('/auth/discord/callback')->assertRedirect(route('home'));

        $this->assertAuthenticated();
        expect(User::sole())
            ->discord_id->toBe('80351110224678912')
            ->discord_username->toBe('jeb_')
            ->name->toBe('Jebediah')
            ->email->toBeNull()
            ->github_id->toBeNull();
    });

    it('falls back to the username without a display name', function () {
        fakeDiscordAccount(globalName: null);

        $this->get('/auth/discord/callback');

        expect(User::sole()->name)->toBe('jeb_');
    });

    it('signs an existing Discord account back in', function () {
        $user = User::factory()->create(['discord_id' => '80351110224678912', 'name' => 'Old']);
        fakeDiscordAccount();

        $this->get('/auth/discord/callback');

        $this->assertAuthenticatedAs($user);
        expect(User::count())->toBe(1)->and($user->fresh()->name)->toBe('Jebediah');
    });

    it('asks Discord only for identify', function () {
        fakeDiscordAccount();

        $this->get(route('discord.login'))->assertRedirect('https://discord.com/oauth2/authorize');
    });
});

describe('linking', function () {
    it('links Discord to a GitHub account and keeps the GitHub profile', function () {
        $user = User::factory()->create(['github_id' => '42', 'github_login' => 'jeb-gh', 'name' => 'jeb-gh', 'avatar' => 'https://github.com/a.png']);
        fakeDiscordAccount();

        $this->actingAs($user)->get('/auth/discord/callback');

        expect(User::count())->toBe(1);
        expect($user->fresh())
            ->discord_id->toBe('80351110224678912')
            ->name->toBe('jeb-gh')
            ->avatar->toBe('https://github.com/a.png');
    });

    it('links GitHub to a Discord account and takes the GitHub profile', function () {
        $user = User::factory()->create(['discord_id' => '80351110224678912', 'name' => 'Jebediah']);
        fakeGitHubAccount();

        $this->actingAs($user)->get('/auth/github/callback');

        $this->assertAuthenticatedAs($user);
        expect(User::count())->toBe(1);
        expect($user->fresh())
            ->github_id->toBe('42')
            ->github_token->toBe('gho_secret')
            ->name->toBe('jeb-gh')
            ->discord_id->toBe('80351110224678912');
    });

    it('returns to the account settings after linking from there', function () {
        $user = User::factory()->create(['github_id' => '42']);
        fakeDiscordAccount();

        $this->actingAs($user)->get(route('discord.link'));
        $this->actingAs($user)->get('/auth/discord/callback')->assertRedirect(route('accounts'));
    });

    it('refuses an account that belongs to someone else, and merges nothing', function (string $provider, array $taken) {
        $other = User::factory()->create($taken);
        $user = User::factory()->create(['github_id' => '7', 'discord_id' => '9']);
        $provider === 'github' ? fakeGitHubAccount() : fakeDiscordAccount();

        $this->actingAs($user)
            ->get("/auth/{$provider}/callback")
            ->assertRedirect(route('accounts'))
            ->assertSessionHasErrors('link');

        $this->assertAuthenticatedAs($user);
        expect($user->fresh()->only(['github_id', 'discord_id']))->toBe(['github_id' => '7', 'discord_id' => '9'])
            ->and($other->fresh()->only(array_keys($taken)))->toBe($taken);
    })->with([
        'GitHub' => ['github', ['github_id' => '42']],
        'Discord' => ['discord', ['discord_id' => '80351110224678912']],
    ]);

    it('keeps the sign-in pages for guests and linking for signed-in users', function () {
        $this->get(route('login'))->assertInertia(fn (Assert $page) => $page->component('Auth/Login'));
        $this->get(route('discord.link'))->assertRedirect(route('login'));
        $this->get(route('accounts'))->assertRedirect(route('login'));

        $this->actingAs(User::factory()->create())->get(route('discord.login'))->assertRedirect(route('home'));
    });
});

describe('unlinking', function () {
    it('unlinks GitHub, token and all, while Discord still signs in', function () {
        $user = User::factory()->create([
            'github_id' => '42', 'github_login' => 'jeb-gh', 'github_token' => 'gho_secret', 'github_scopes' => 'public_repo',
            'discord_id' => '80351110224678912',
        ]);

        $this->actingAs($user)->delete(route('accounts.unlink', 'github'))->assertSessionHasNoErrors();

        expect($user->fresh()->only(['github_id', 'github_login', 'github_token', 'github_scopes']))
            ->each->toBeNull();
    });

    it('refuses to unlink the only way to sign in', function () {
        $user = User::factory()->create(['discord_id' => '80351110224678912']);

        $this->actingAs($user)->delete(route('accounts.unlink', 'discord'))->assertSessionHasErrors('link');

        expect($user->fresh()->discord_id)->toBe('80351110224678912');
    });

    it('shows what is linked', function () {
        $user = User::factory()->create(['github_id' => '42', 'github_login' => 'jeb-gh', 'github_token' => 'gho_x', 'github_scopes' => 'public_repo']);

        $this->actingAs($user)->get(route('accounts'))->assertInertia(fn (Assert $page) => $page
            ->component('Settings/Accounts')
            ->where('github', ['login' => 'jeb-gh', 'canOpenPullRequests' => true])
            ->where('discord', null));
    });
});

it('tells a Discord-only account to link GitHub before editing, without sending GitHub an empty token', function () {
    Http::fake([
        ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => []]),
        'api.github.com/repos/KSAModding/content-index/contents/*' => Http::response(['content' => base64_encode(
            "spec_version = 1\nid = \"Rockets\"\ntype = \"mod\"\nname = \"Rockets\"\n\n[releases]\ngithub = \"org/rockets\"\n"
        )]),
    ]);
    $user = User::factory()->create(['discord_id' => '80351110224678912']);

    $this->actingAs($user)->get(route('listings.edit', 'Rockets'))->assertInertia(fn (Assert $page) => $page
        ->component('Listings/NotOwner')
        ->where('githubLogin', null));

    Http::assertNotSent(fn (Request $request) => $request->hasHeader('Authorization'));
});
