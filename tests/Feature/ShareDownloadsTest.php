<?php

use App\Enums\Role;
use App\Models\Share;
use App\Models\User;
use App\Support\Share\Signer;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Storage;

uses(RefreshDatabase::class);

function downloadableShare(array $attributes = [], ?array $release = []): Share
{
    $share = Share::factory()->create($attributes);
    $share->releases()->create([
        'version' => '1.0.0', 'status' => 'stable', 'path' => 'archives/x.zip', 'size' => 1, 'sha256' => str_repeat('0', 64),
        'folder' => $share->name, 'game_version' => '2026.10.10.5554', 'dependencies' => [], ...$release,
    ]);

    return $share;
}

it('counts a player once a day, without touching when the listing changed', function () {
    $share = downloadableShare(['updated_at' => now()->subWeek()]);
    $url = route('share.api.downloads', ['type' => $share->type, 'id' => $share->listing_id]);

    $this->post($url)->assertNoContent();
    $this->post($url)->assertNoContent();
    $this->withServerVariables(['REMOTE_ADDR' => '10.0.0.2'])->post($url)->assertNoContent();

    expect($share->fresh())
        ->downloads->toBe(2)
        ->updated_at->toDateString()->toBe(now()->subWeek()->toDateString());

    // The next day, the same player counts again.
    $this->travel(1)->day();
    $this->post($url)->assertNoContent();
    expect($share->fresh()->downloads)->toBe(3);
});

it('does not count a listing with nothing left to download', function () {
    $share = downloadableShare(release: ['taken_down_at' => now()]);

    $this->post(route('share.api.downloads', ['type' => $share->type, 'id' => $share->listing_id]))->assertNotFound();
    expect($share->fresh()->downloads)->toBe(0);
});

it('orders the list by downloads when asked', function () {
    $this->withoutVite();
    downloadableShare(['listing_id' => 'Quiet', 'name' => 'Quiet']);
    downloadableShare(['listing_id' => 'Popular', 'name' => 'Popular', 'downloads' => 9, 'updated_at' => now()->subYear()]);

    $this->get(route('shares.index', ['types' => 'vehicles', 'sort' => 'downloads']))
        ->assertInertia(fn ($page) => $page
            ->where('sort', 'downloads')
            ->where('shares.data.0.id', 'Popular')
            ->where('shares.data.0.downloads', 9));

    $this->get(route('shares.index', 'vehicles'))
        ->assertInertia(fn ($page) => $page->where('shares.data.0.id', 'Quiet'));
});

it('counts an IPv6 connection once, whichever address of its /64 it uses', function () {
    $share = downloadableShare();
    $url = route('share.api.downloads', ['type' => $share->type, 'id' => $share->listing_id]);

    foreach (['2001:db8:1:2::1', '2001:db8:1:2:ffff::9', '2001:db8:1:3::1'] as $ip) {
        $this->withServerVariables(['REMOTE_ADDR' => $ip])->post($url)->assertNoContent();
    }

    expect($share->fresh()->downloads)->toBe(2);
});

it('counts the version a download names, or else the newest release still up', function () {
    $share = downloadableShare();
    $share->releases()->create([
        'version' => '1.1.0', 'status' => 'stable', 'path' => 'archives/y.zip', 'size' => 1, 'sha256' => str_repeat('0', 64),
        'folder' => $share->name, 'game_version' => '2026.10.10.5554', 'dependencies' => [],
    ]);
    $url = route('share.api.downloads', ['type' => $share->type, 'id' => $share->listing_id]);

    $this->withServerVariables(['REMOTE_ADDR' => '10.0.0.1'])->postJson($url, ['version' => '1.0.0'])->assertNoContent();
    $this->withServerVariables(['REMOTE_ADDR' => '10.0.0.2'])->post($url)->assertNoContent();
    $this->withServerVariables(['REMOTE_ADDR' => '10.0.0.3'])->postJson($url, ['version' => '9.9.9'])->assertNoContent();

    expect($share->fresh()->downloads)->toBe(3)
        ->and($share->releases()->pluck('downloads', 'version')->all())->toBe(['1.0.0' => 1, '1.1.0' => 2]);
});

it('publishes the counts in the signed release list once a day, only for lists whose total changed', function () {
    $this->freezeTime();
    Storage::fake('public');
    config(['share.disk' => 'public', 'share.signing_key' => Signer::generate()]);
    $share = downloadableShare(release: ['listed_at' => now()]);
    $quiet = downloadableShare(['listing_id' => 'Quiet'], ['listed_at' => now()]);
    $this->post(route('share.api.downloads', ['type' => $share->type, 'id' => $share->listing_id]))->assertNoContent();

    $this->artisan('shares:publish-downloads')->expectsOutput('Published the download counts of 1 listings.')->assertSuccessful();

    $path = "releases/{$share->type}/{$share->listing_id}.json";
    $bytes = (string) Storage::disk('public')->get($path);
    expect(Signer::verify($bytes, (string) Storage::disk('public')->get("{$path}.sig"), Signer::publicKey()))->toBeTrue()
        ->and(json_decode($bytes, true)['downloads'])->toBe(['total' => 1, 'versions' => ['1.0.0' => 1], 'updated' => now()->utc()->format('Y-m-d\TH:i:s\Z')])
        ->and(Storage::disk('public')->exists("releases/{$quiet->type}/Quiet.json"))->toBeFalse();

    // Nothing new: nothing to sign again, unless asked to sign every list.
    $this->artisan('shares:publish-downloads')->expectsOutput('Published the download counts of 0 listings.');
    $this->artisan('shares:publish-downloads --all')->expectsOutput('Published the download counts of 2 listings.');
    expect(Storage::disk('public')->exists("releases/{$quiet->type}/Quiet.json"))->toBeTrue();
});

it('publishes the counts from the admin share panel, for admins only', function () {
    Storage::fake('public');
    config(['share.disk' => 'public', 'share.signing_key' => Signer::generate()]);
    $share = downloadableShare(['downloads' => 4], ['listed_at' => now()]);
    downloadableShare(['listing_id' => 'Quiet'], ['listed_at' => now()]);

    $this->actingAs(User::factory()->create(['role' => Role::Moderator]))->post(route('admin.shares.downloads'))->assertForbidden();

    $admin = User::factory()->create(['role' => Role::Admin]);
    $this->actingAs($admin)->post(route('admin.shares.downloads'))
        ->assertSessionHasNoErrors()
        ->assertInertiaFlash('status', 'Published the download counts of 1 listing.');
    expect($share->fresh()->published_downloads)->toBe(4);

    $this->actingAs($admin)->post(route('admin.shares.downloads'), ['all' => true])
        ->assertInertiaFlash('status', 'Published the download counts of 2 listings.');
});
