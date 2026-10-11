<?php

use App\Models\User;
use Illuminate\Foundation\Inspiring;
use Illuminate\Support\Facades\Artisan;

Artisan::command('inspire', function () {
    $this->comment(Inspiring::quote());
})->purpose('Display an inspiring quote');

Artisan::command('ksamods:admin {account : A GitHub login, Discord username or user id} {--revoke}', function (string $account) {
    $user = User::where('github_login', $account)
        ->orWhere('discord_username', $account)
        ->orWhere('id', ctype_digit($account) ? (int) $account : 0)
        ->first();

    if ($user === null) {
        $this->error("No user is linked to {$account}.");

        return 1;
    }

    $user->forceFill(['is_admin' => ! $this->option('revoke')])->save();
    $this->info(($user->is_admin ? 'Made ' : 'Removed admin from ')."{$user->name} (user {$user->id}).");

    return 0;
})->purpose('Make a user an admin, who can delete any review or comment');
