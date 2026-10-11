<?php

namespace App\Http\Controllers;

use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Inertia\Inertia;
use Inertia\Response;

/**
 * The sign-in accounts linked to a ksamods.gg account: GitHub, Discord or both.
 */
class AccountController extends Controller
{
    /**
     * What unlinking each provider clears.
     */
    private const COLUMNS = [
        'github' => ['github_id', 'github_login', 'github_token', 'github_scopes'],
        'discord' => ['discord_id', 'discord_username'],
    ];

    public function show(Request $request): Response
    {
        $user = $request->user();

        return Inertia::render('Settings/Accounts', [
            'github' => $user->github_id === null ? null : [
                'login' => $user->github_login,
                'canOpenPullRequests' => $user->canOpenPullRequests(),
            ],
            'discord' => $user->discord_id === null ? null : [
                'username' => $user->discord_username,
            ],
        ]);
    }

    public function unlink(Request $request, string $provider): RedirectResponse
    {
        abort_unless(isset(self::COLUMNS[$provider]), 404);

        $user = $request->user();
        $other = $provider === 'github' ? 'discord_id' : 'github_id';

        // Without another linked account, nobody could sign in to this one again.
        if ($user->{$other} === null) {
            return back()->withErrors(['link' => 'Link another account before you unlink your only way to sign in.']);
        }

        $user->forceFill(array_fill_keys(self::COLUMNS[$provider], null))->save();

        return back();
    }
}
