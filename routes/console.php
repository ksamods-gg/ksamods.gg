<?php

use App\Enums\Role;
use App\Models\Share;
use App\Models\ShareUpload;
use App\Models\User;
use App\Support\ContentIndex;
use App\Support\Share\ArchiveMirror;
use App\Support\Share\DataFiles;
use App\Support\Share\ShareBot;
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

/*
 * The table of a host without GitHub storage (RFC 0104), in the key order and layout of the
 * example in content-index's hosts.toml, checked against what hosts.schema.json accepts.
 */
Artisan::command('share:host', function () {
    $base = rtrim((string) config('app.url'), '/');
    $data = (string) config('share.public_url');
    $bot = (int) config('share.bot_id');
    $problems = array_filter([
        str_starts_with($base, 'https://') ? null : "APP_URL must be https, not {$base}.",
        str_starts_with($data, 'https://') ? null : "SHARE_PUBLIC_URL must be the bucket's https address, not {$data}.",
        $bot >= 1 ? null : 'SHARE_BOT_ID must be the numeric id of the bot GitHub account.',
    ]);

    try {
        $key = Signer::publicKey();
    } catch (RuntimeException $error) {
        $problems[] = $error->getMessage();
    }

    if ($problems !== []) {
        foreach ($problems as $problem) {
            $this->error($problem);
        }

        return 1;
    }

    $string = fn (string $value) => json_encode($value, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
    $list = fn (array $values) => '['.implode(', ', array_map($string, $values)).']';
    $limits = config('share.max_bytes');
    $sizes = is_array($limits) ? implode(', ', array_map(fn ($type, $bytes) => "{$type} = {$bytes}", array_keys($limits), $limits)) : '';

    $this->line(implode("\n", [
        '['.config('share.key').']',
        'kind = "share"',
        'name = '.$string((string) parse_url($base, PHP_URL_HOST)),
        'data = '.$string($data),
        'downloads = '.$list([$base.'/share/files/', $data.'archives/']),
        'key = '.$string($key),
        'retired_keys = '.$list(config('share.retired_keys')),
        "bot = {$bot}",
        'api = '.$string($base.'/api/share/v1/'),
        'types = '.$list(config('share.types')),
        "max_bytes = { {$sizes} }",
        'new_listings_per_day = '.(int) config('share.new_listings_per_day'),
        'policy = '.$string($base.route('policy', absolute: false)),
    ]));

    return 0;
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

Artisan::command('share:purge {type : vehicle or save} {id : The listing id} {--force : Do not ask}', function (string $type, string $id) {
    $share = Share::where('type', $type)->where('listing_id', $id)->first();

    if ($share === null) {
        $this->error("There is no {$type} {$id}.");

        return 1;
    }

    // Only a listing that never reached the index: once it did, a release can only be taken down (rule 18).
    if (in_array($id, array_column(ContentIndex::shared($share->type), 'id'), true) || ShareBot::merged($share)) {
        $this->error("The {$type} {$id} is in the index, or its pull request was merged. Take its releases down instead.");

        return 1;
    }

    if (! $this->option('force') && ! $this->confirm("Delete the {$type} {$id}, its {$share->releases()->count()} releases, data files and pull request for good?")) {
        return 1;
    }

    ShareBot::withdraw($share);

    foreach ($share->releases as $release) {
        DataFiles::disk()->delete($release->path);

        if ($release->mirror_url !== null) {
            ArchiveMirror::delete($release);
        }
    }

    foreach (["releases/{$type}/{$id}.json", "owners/{$type}/{$id}.json"] as $path) {
        DataFiles::disk()->delete([$path, "{$path}.sig"]);
    }

    $share->delete();
    $this->info("Deleted the {$type} {$id}. Its id is free again.");

    return 0;
})->purpose('Delete a shared vehicle or save that never reached the index, with everything it left');
