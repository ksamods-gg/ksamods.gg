<?php

use App\Enums\Role;
use App\Models\User;
use Illuminate\Foundation\Inspiring;
use Illuminate\Support\Facades\Artisan;

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
