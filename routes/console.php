<?php

use App\Enums\Role;
use App\Models\Share;
use App\Models\ShareUpload;
use App\Models\User;
use App\Support\Share\DataFiles;
use App\Support\Share\Signer;
use App\Support\Toml;
use Illuminate\Foundation\Inspiring;
use Illuminate\Support\Facades\Artisan;
use Illuminate\Support\Facades\Schedule;

Artisan::command('inspire', function () {
    $this->comment(Inspiring::quote());
})->purpose('Display an inspiring quote');

Artisan::command('ksamods:admin {account : A GitHub login, Discord username or user id} {--moderator : Make them a moderator instead} {--revoke : Remove their role}', function (string $account) {
    $user = User::where('github_login', $account)
        ->orWhere('discord_username', $account)
        ->orWhere('id', ctype_digit($account) ? (int) $account : 0)
        ->first();

    if ($user === null) {
        $this->error("No user is linked to {$account}.");

        return 1;
    }

    $role = match (true) {
        (bool) $this->option('revoke') => null,
        (bool) $this->option('moderator') => Role::Moderator,
        default => Role::Admin,
    };
    $user->forceFill(['role' => $role])->save();
    $this->info($role ? "Made {$user->name} (user {$user->id}) {$role->value}." : "Removed the role of {$user->name} (user {$user->id}).");

    return 0;
})->purpose('Make a user an admin or moderator, who can open the admin panel');

/*
 * The share host (RFC 0094, RFC 0104). Discarding expired uploads (rule 15) and spent sign-ins.
 */
Schedule::command('model:prune', ['--model' => [ShareUpload::class]])->hourly();
Schedule::command('passport:purge')->daily();

Artisan::command('share:key', function () {
    $this->line('SHARE_SIGNING_KEY='.Signer::generate());
    $this->newLine();
    $this->comment('Keep this secret, in the app and queue environment only. After setting it, run share:host for the public key.');
})->purpose('Make an Ed25519 signing key for the share host\'s data files');

Artisan::command('share:host', function () {
    $base = rtrim((string) config('app.url'), '/');
    $table = [
        'kind' => 'share',
        'name' => parse_url($base, PHP_URL_HOST),
        'data' => config('share.public_url'),
        'downloads' => [$base.'/share/files/', config('share.public_url').'archives/'],
        'key' => Signer::publicKey(),
        'retired_keys' => config('share.retired_keys'),
        'bot' => config('share.bot_id'),
        'api' => $base.'/api/share/v1/',
        'types' => config('share.types'),
        'max_bytes' => config('share.max_bytes'),
        'new_listings_per_day' => config('share.new_listings_per_day'),
        'policy' => route('policy'),
    ];

    $this->line(Toml::encode([config('share.key') => $table]));
})->purpose('Print the hosts.toml table that registers this share host');

Artisan::command('share:resign', function () {
    $shares = Share::with('releases', 'user')->get();

    foreach ($shares as $share) {
        DataFiles::writeReleaseList($share);
        DataFiles::writeOwnerRecord($share);
    }

    // Attestations are written once, so their signatures are renewed from the stored bytes.
    foreach (DataFiles::disk()->files('attestations') as $path) {
        if (str_ends_with($path, '.json')) {
            DataFiles::disk()->put("{$path}.sig", Signer::sign((string) DataFiles::read($path))."\n", ['visibility' => 'public']);
        }
    }

    $this->info("Signed again: {$shares->count()} listings and their attestations.");
})->purpose('Sign every data file again with the current key, after a key rotation (rule 24)');
