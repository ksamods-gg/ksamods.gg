<?php

namespace App\Support;

use App\Models\User;
use Illuminate\Support\Facades\Auth;
use Illuminate\Validation\ValidationException;

/**
 * Signs in with, or links, a GitHub or Discord account. Accounts are matched by the provider's
 * own id only, never by email, and two ksamods.gg accounts are never merged.
 */
class SocialAccounts
{
    /**
     * @param  'github_id'|'discord_id'  $idColumn
     * @param  array<string, mixed>  $account  The provider's own columns, refreshed on every sign-in.
     * @param  array<string, mixed>  $profile  Name, avatar and email, which the caller decides whether to apply.
     *
     * @throws ValidationException When the account is linked to another user.
     */
    public static function connect(string $idColumn, string $id, array $account, array $profile, bool $applyProfile): User
    {
        $owner = User::where($idColumn, $id)->first();
        $current = Auth::user();

        if ($current instanceof User) {
            if ($owner !== null && ! $owner->is($current)) {
                throw ValidationException::withMessages([
                    'link' => 'That account is already linked to another ksamods.gg account. Log in with it and unlink it there first.',
                ]);
            }

            $user = $current;
        } else {
            $user = $owner ?? new User;
        }

        $user->fill([$idColumn => $id, ...$account]);

        // A new account always takes the provider's profile.
        if ($applyProfile || ! $user->exists) {
            $user->fill(array_filter($profile, fn ($value) => $value !== null));
        }

        $user->save();

        if (! $current) {
            Auth::login($user, remember: true);
        }

        return $user;
    }
}
