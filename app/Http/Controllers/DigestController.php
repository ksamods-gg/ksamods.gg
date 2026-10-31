<?php

namespace App\Http\Controllers;

use App\Mail\ConfirmDigestEmail;
use App\Models\User;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Mail;
use Illuminate\Validation\Rule;
use Inertia\Inertia;
use Inertia\Response;

/**
 * The email digest of new releases from followed mods, vehicles and saves: its address, which the
 * player confirms by email first, and how often it comes.
 */
class DigestController extends Controller
{
    public const FREQUENCIES = ['off', 'daily', 'weekly', 'monthly'];

    public function show(Request $request): Response
    {
        $user = $request->user();

        return Inertia::render('Settings/Notifications', [
            // Prefilled with the sign-in email until the player chooses one.
            'email' => $user->digest_email ?? $user->email,
            'confirmed' => $user->digest_email !== null && $user->digest_email_verified_at !== null,
            'pending' => $user->digest_email !== null && $user->digest_email_verified_at === null,
            'frequency' => $user->digest_frequency,
            'following' => $user->follows()->count(),
        ]);
    }

    public function update(Request $request): RedirectResponse
    {
        $user = $request->user();
        $validated = $request->validate([
            'email' => ['nullable', 'email:rfc', 'max:254'],
            'frequency' => ['required', Rule::in(self::FREQUENCIES)],
        ]);
        $email = $validated['email'] ?: null;

        $user->forceFill(['digest_frequency' => $validated['frequency']]);

        // A new address waits for its confirmation before anything is sent to it.
        if ($email !== $user->digest_email) {
            $user->forceFill(['digest_email' => $email, 'digest_email_verified_at' => null]);

            if ($email !== null) {
                Mail::to($email)->send(new ConfirmDigestEmail($user));
            }
        }

        $user->save();
        Inertia::flash('status', $user->digest_email !== null && $user->digest_email_verified_at === null
            ? "Saved. Click the link we sent to {$user->digest_email} to start the digest."
            : 'Saved.');

        return back();
    }

    /**
     * The link in the confirmation email. Signed, and only for the address it was sent to.
     */
    public function confirm(Request $request, User $user): Response
    {
        abort_unless(hash_equals(sha1((string) $user->digest_email), (string) $request->query('email')), 403);

        $user->forceFill(['digest_email_verified_at' => now()])->save();

        return Inertia::render('Settings/DigestDone', [
            'title' => 'Email confirmed',
            'message' => $user->digest_frequency === 'off'
                ? "{$user->digest_email} is confirmed. Choose how often the digest comes in your notification settings."
                : "{$user->digest_email} is confirmed. Your {$user->digest_frequency} digest starts with the next one.",
        ]);
    }

    /**
     * The link in every digest: turns the digest off, without signing in.
     */
    public function unsubscribe(User $user): Response
    {
        $user->forceFill(['digest_frequency' => 'off'])->save();

        return Inertia::render('Settings/DigestDone', [
            'title' => 'Unsubscribed',
            'message' => 'You will get no more digest emails. Turn them back on in your notification settings whenever you like.',
        ]);
    }
}
