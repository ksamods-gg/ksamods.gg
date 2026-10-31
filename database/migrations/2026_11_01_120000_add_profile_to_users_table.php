<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * A public profile at /u/<username>, off until the player turns it on: its username, bio,
     * links, whether it shows the linked GitHub and Discord names, which sections it shows, and
     * the mods the player's last "My mods" visit found they control.
     */
    public function up(): void
    {
        Schema::table('users', function (Blueprint $table) {
            $table->string('username', 30)->nullable()->unique();
            $table->boolean('profile_enabled')->default(false);
            $table->text('bio')->nullable();
            $table->json('profile_links')->nullable();
            $table->boolean('profile_show_accounts')->default(false);
            $table->json('profile_sections')->nullable();
            $table->json('mod_ids')->nullable();
        });
    }

    public function down(): void
    {
        Schema::table('users', function (Blueprint $table) {
            $table->dropUnique(['username']);
            $table->dropColumn(['username', 'profile_enabled', 'bio', 'profile_links', 'profile_show_accounts', 'profile_sections', 'mod_ids']);
        });
    }
};
