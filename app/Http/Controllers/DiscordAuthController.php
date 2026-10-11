<?php

namespace App\Http\Controllers;

use App\Support\SocialAccounts;
use Illuminate\Http\RedirectResponse;
use Illuminate\Validation\ValidationException;
use Laravel\Socialite\Facades\Socialite;
use LogicException;
use SocialiteProviders\Discord\Provider as DiscordProvider;
use Symfony\Component\HttpFoundation\RedirectResponse as SymfonyRedirectResponse;

class DiscordAuthController extends Controller
{
    public function redirect(): SymfonyRedirectResponse
    {
        return $this->discord()->redirect();
    }

    /**
     * Links Discord to the signed-in account, then returns to the account settings.
     */
    public function link(): SymfonyRedirectResponse
    {
        redirect()->setIntendedUrl(route('accounts'));

        return $this->discord()->redirect();
    }

    public function callback(): RedirectResponse
    {
        $discordUser = $this->discord()->user();
        $user = auth()->user();

        try {
            SocialAccounts::connect(
                'discord_id',
                (string) $discordUser->getId(),
                ['discord_username' => $discordUser->getNickname()],
                [
                    // The display name Discord shows people, or else the username.
                    'name' => $discordUser->getRaw()['global_name'] ?? $discordUser->getName(),
                    'avatar' => $discordUser->getAvatar(),
                ],
                // GitHub's profile wins on an account that has it.
                applyProfile: $user === null || $user->github_id === null,
            );
        } catch (ValidationException $e) {
            return redirect()->route('accounts')->withErrors($e->errors());
        }

        return redirect()->intended(route('home'));
    }

    /**
     * Only identify: accounts are matched by Discord id, never by email.
     */
    private function discord(): DiscordProvider
    {
        $provider = Socialite::driver('discord');

        throw_unless($provider instanceof DiscordProvider, LogicException::class, 'The discord driver is not the Discord provider.');

        return $provider->setScopes(['identify']);
    }
}
