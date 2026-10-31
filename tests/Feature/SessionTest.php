<?php

use App\Mail\NewSignIn;
use App\Models\User;
use App\Support\Device;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Mail;

uses(RefreshDatabase::class);

beforeEach(fn () => $this->withoutVite());

const FIREFOX = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0';
const CHROME_ANDROID = 'Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36';

it('names a browser from its user agent', function () {
    expect(Device::describe(FIREFOX))->toBe('Firefox on Windows')
        ->and(Device::describe(CHROME_ANDROID))->toBe('Chrome on Android')
        ->and(Device::describe(null))->toBe('A browser');
});

it('emails a sign-in from a new browser to the confirmed address, but not the first sign-in', function () {
    Mail::fake();
    $user = User::factory()->create(['digest_email' => 'pilot@example.com', 'digest_email_verified_at' => now()]);
    request()->headers->set('User-Agent', FIREFOX);

    Auth::login($user);
    Mail::assertNothingQueued();

    // The same browser again: nothing. Another one: an email.
    Auth::login($user);
    request()->headers->set('User-Agent', CHROME_ANDROID);
    Auth::login($user);

    Mail::assertQueued(NewSignIn::class, 1);
    Mail::assertQueued(NewSignIn::class, fn (NewSignIn $mail) => $mail->hasTo('pilot@example.com') && $mail->device === 'Chrome on Android');
});

it('signs out another session of the player, and nobody else\'s', function () {
    config(['session.driver' => 'database']);
    [$user, $other] = User::factory()->count(2)->create();
    foreach ([['a', $user->id], ['b', $other->id]] as [$id, $owner]) {
        DB::table('sessions')->insert(['id' => $id, 'user_id' => $owner, 'user_agent' => FIREFOX, 'payload' => '', 'last_activity' => time()]);
    }

    $this->actingAs($user)->delete('/settings/sessions/a')->assertRedirect();
    $this->actingAs($user)->delete('/settings/sessions/b')->assertRedirect();

    // The test's own request keeps a session row too.
    expect(DB::table('sessions')->where('id', 'a')->exists())->toBeFalse()
        ->and(DB::table('sessions')->where('id', 'b')->exists())->toBeTrue();
});

it('signs out every other session and replaces the remember token', function () {
    config(['session.driver' => 'database']);
    $user = User::factory()->create(['remember_token' => 'old-token']);
    DB::table('sessions')->insert(['id' => 'elsewhere', 'user_id' => $user->id, 'user_agent' => FIREFOX, 'payload' => '', 'last_activity' => time()]);

    $this->actingAs($user)->delete('/settings/sessions')->assertRedirect();

    expect(DB::table('sessions')->where('id', 'elsewhere')->exists())->toBeFalse()
        ->and($user->fresh()->remember_token)->not->toBe('old-token');
});
