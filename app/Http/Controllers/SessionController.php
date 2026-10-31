<?php

namespace App\Http\Controllers;

use App\Support\Device;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Inertia\Inertia;
use Inertia\Response;

/**
 * The browsers signed in to the player's account, and signing them out.
 */
class SessionController extends Controller
{
    public function index(Request $request): Response
    {
        $current = $request->session()->getId();

        return Inertia::render('Settings/Sessions', [
            // Only the database driver keeps a list of sessions to show.
            'supported' => config('session.driver') === 'database',
            'sessions' => config('session.driver') !== 'database' ? [] : DB::table('sessions')
                ->where('user_id', $request->user()->id)
                ->orderByDesc('last_activity')
                ->get()
                ->map(fn (object $session) => [
                    'id' => $session->id,
                    'device' => Device::describe($session->user_agent),
                    'ip' => $session->ip_address,
                    'last_active' => Carbon::createFromTimestamp($session->last_activity)->toIso8601String(),
                    'current' => $session->id === $current,
                ]),
        ]);
    }

    public function destroy(Request $request, string $id): RedirectResponse
    {
        abort_if($id === $request->session()->getId(), 422);

        DB::table('sessions')->where('id', $id)->where('user_id', $request->user()->id)->delete();
        Inertia::flash('status', 'Signed out of that session.');

        return back();
    }

    /**
     * Signs out every other browser: their sessions go, and a new remember token stops their
     * "remember me" cookies from signing them back in. This browser gets a fresh cookie.
     */
    public function destroyOthers(Request $request): RedirectResponse
    {
        $user = $request->user();

        DB::table('sessions')->where('user_id', $user->id)->where('id', '!=', $request->session()->getId())->delete();
        $user->setRememberToken(Str::random(60));
        $user->save();
        Auth::login($user, remember: true);

        Inertia::flash('status', 'Signed out of every other session.');

        return back();
    }
}
