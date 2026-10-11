<?php

namespace App\Http\Controllers;

use App\Models\User;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Laravel\Socialite\Facades\Socialite;
use Laravel\Socialite\Two\GithubProvider;
use Laravel\Socialite\Two\User as OAuthUser;
use LogicException;
use Symfony\Component\HttpFoundation\RedirectResponse as SymfonyRedirectResponse;

class GitHubAuthController extends Controller
{
    public function redirect(): SymfonyRedirectResponse
    {
        return $this->github()->redirect();
    }

    /**
     * Asked for only when submitting a listing, because public_repo can write to every public repository of the user.
     */
    public function authorizePullRequests(): SymfonyRedirectResponse
    {
        // Back to the form the user came from, a new listing or an edit.
        redirect()->setIntendedUrl(url()->previous(route('listings.create')));
        session()->put('github.requested_pull_requests', true);

        return $this->github()->scopes(['public_repo'])->redirect();
    }

    public function callback(): RedirectResponse
    {
        $githubUser = $this->github()->user();
        throw_unless($githubUser instanceof OAuthUser, LogicException::class, 'GitHub did not return an OAuth 2 user.');

        $user = User::updateOrCreate(
            ['github_id' => $githubUser->getId()],
            [
                'github_login' => $githubUser->getNickname(),
                'github_token' => $githubUser->token,
                'github_scopes' => implode(',', $githubUser->approvedScopes),
                'name' => $githubUser->getNickname(),
                'email' => $githubUser->getEmail(),
                'avatar' => $githubUser->getAvatar(),
            ],
        );

        Auth::login($user, remember: true);

        return redirect()->intended(route('home'));
    }

    public function logout(Request $request): RedirectResponse
    {
        Auth::logout();

        $request->session()->invalidate();
        $request->session()->regenerateToken();

        return redirect()->route('home');
    }

    private function github(): GithubProvider
    {
        $provider = Socialite::driver('github');

        throw_unless($provider instanceof GithubProvider, LogicException::class, 'The github driver is not the GitHub provider.');

        return $provider;
    }
}
