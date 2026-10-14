<?php

namespace App\Http\Controllers;

use App\Models\Comment;
use App\Models\Report;
use App\Models\Review;
use App\Notifications\CommentActivity;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Notifications\DatabaseNotification;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\DB;
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

    /**
     * Deletes the account. Reviews, comments (with the replies under them) and the user's own
     * reports go with it through the foreign keys, which skip model events; so what those events
     * would do happens here: open reports about the posts are resolved, and the notifications
     * other people got about the user's comments, which quote them, are deleted.
     */
    public function destroy(Request $request): RedirectResponse
    {
        $user = $request->user();

        DB::transaction(function () use ($user) {
            $commentIds = $user->comments()->withTrashed()->pluck('id');
            $reviewIds = $user->reviews()->pluck('id');

            Report::open()
                ->where(fn ($query) => $query
                    ->where(fn ($query) => $query->where('reportable_type', (new Comment)->getMorphClass())->whereIn('reportable_id', $commentIds))
                    ->orWhere(fn ($query) => $query->where('reportable_type', (new Review)->getMorphClass())->whereIn('reportable_id', $reviewIds)))
                ->update(['resolved_at' => now()]);
            DatabaseNotification::query()
                ->where('type', CommentActivity::class)
                ->whereIn('data->comment_id', $commentIds)
                ->delete();
            $user->notifications()->delete();
            $user->delete();
        });

        Auth::logout();
        $request->session()->invalidate();
        $request->session()->regenerateToken();

        return redirect()->route('home');
    }
}
