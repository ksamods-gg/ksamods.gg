<?php

use App\Mail\ConfirmDigestEmail;
use App\Mail\ReleaseDigest;
use App\Models\Follow;
use App\Models\SeenRelease;
use App\Models\User;
use App\Notifications\SiteActivity;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Mail;
use Illuminate\Support\Facades\URL;

uses(RefreshDatabase::class);

beforeEach(fn () => $this->withoutVite());

// The index with one mod, at the given versions, newest last. A later call changes the answer.
function indexWithRockets(string ...$versions): void
{
    test()->versions = $versions;
    Http::fake([ContentIndex::URL => fn () => Http::response(['tags' => ['mod' => []], 'listings' => [[
        'id' => 'Rockets', 'published_at' => '2026-01-01T00:00:00Z', 'updated_at' => '2026-01-01T00:00:00Z',
        'authored' => ['type' => 'mod', 'name' => 'Rockets'],
        'releases' => array_map(fn (string $version, int $day) => [
            'version' => $version, 'release_status' => 'stable', 'release_date' => sprintf('2026-01-%02dT00:00:00Z', $day + 1),
            'download' => ['url' => "https://example.com/{$version}.zip"],
        ], test()->versions, array_keys(test()->versions)),
    ]]])]);
    ContentIndex::refresh();
}

it('follows a mod and stops following it, noting its newest version', function () {
    indexWithRockets('1.0.0');
    $user = User::factory()->create();

    $this->actingAs($user)->post('/follows', ['kind' => 'mod', 'id' => 'Rockets'])->assertRedirect();
    expect($user->isFollowing('mod', 'Rockets'))->toBeTrue()
        ->and(SeenRelease::find('Rockets')->version)->toBe('1.0.0');

    $this->actingAs($user)->post('/follows', ['kind' => 'mod', 'id' => 'Rockets']);
    expect($user->isFollowing('mod', 'Rockets'))->toBeFalse();
});

it('tells the followers of a mod about its new release, once', function () {
    indexWithRockets('1.0.0');
    $follower = User::factory()->create();
    $this->actingAs($follower)->post('/follows', ['kind' => 'mod', 'id' => 'Rockets']);

    $this->artisan('follows:check')->assertSuccessful();
    expect($follower->notifications()->count())->toBe(0);

    indexWithRockets('1.0.0', '1.1.0');
    $this->artisan('follows:check')->assertSuccessful();
    $this->artisan('follows:check')->assertSuccessful();

    expect($follower->notifications()->sole()->data)->toMatchArray(['kind' => 'release', 'version' => '1.1.0', 'url' => route('mods.show', 'Rockets')]);
});

it('tells the followers of a vehicle about its new release, but not its owner', function () {
    [$owner, $follower] = User::factory()->count(2)->create();
    Follow::create(['user_id' => $follower->id, 'kind' => 'vehicle', 'listing_id' => 'Rover']);
    Follow::create(['user_id' => $owner->id, 'kind' => 'vehicle', 'listing_id' => 'Rover']);

    Follow::announce('vehicle', 'Rover', 'Rover', '1.1.0', 'https://ksamods.test/vehicles/Rover', except: $owner->id);

    expect($follower->notifications()->count())->toBe(1)->and($owner->notifications()->count())->toBe(0);
});

it('confirms the digest address by email before using it', function () {
    Mail::fake();
    $user = User::factory()->create();

    $this->actingAs($user)->put('/settings/notifications', ['email' => 'pilot@example.com', 'frequency' => 'weekly'])->assertRedirect();

    Mail::assertSent(ConfirmDigestEmail::class, fn (ConfirmDigestEmail $mail) => $mail->hasTo('pilot@example.com'));
    expect($user->fresh())->digest_frequency->toBe('weekly')->digest_email_verified_at->toBeNull();

    // A link for another address does nothing.
    $this->get(URL::signedRoute('digest.confirm', ['user' => $user->id, 'email' => sha1('other@example.com')]))->assertForbidden();
    $this->get(URL::signedRoute('digest.confirm', ['user' => $user->id, 'email' => sha1('pilot@example.com')]))->assertOk();

    expect($user->fresh()->digest_email_verified_at)->not->toBeNull();
});

it('emails the digest of new releases to confirmed addresses only, and unsubscribes in one click', function () {
    Mail::fake();
    $confirmed = User::factory()->create(['digest_email' => 'a@example.com', 'digest_email_verified_at' => now(), 'digest_frequency' => 'weekly']);
    $unconfirmed = User::factory()->create(['digest_email' => 'b@example.com', 'digest_frequency' => 'weekly']);

    foreach ([$confirmed, $unconfirmed] as $user) {
        $user->notify(new SiteActivity('release', 'Rockets', 'https://ksamods.test/mods/Rockets', '1.1.0'));
    }

    $this->artisan('digests:send')->assertSuccessful();
    // Not again within the week.
    $this->artisan('digests:send')->assertSuccessful();

    Mail::assertSent(ReleaseDigest::class, 1);
    Mail::assertSent(ReleaseDigest::class, fn (ReleaseDigest $mail) => $mail->hasTo('a@example.com') && $mail->releases[0]['version'] === '1.1.0');

    $this->post(URL::signedRoute('digest.unsubscribe', ['user' => $confirmed->id]))->assertOk();
    expect($confirmed->fresh()->digest_frequency)->toBe('off');
});
