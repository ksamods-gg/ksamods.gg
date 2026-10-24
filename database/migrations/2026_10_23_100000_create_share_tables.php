<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Vehicles and saves shared through ksamods.gg as a share host (RFC 0094).
     */
    public function up(): void
    {
        // The host's opaque, stable id for an account, as owner records name it. It carries no name.
        Schema::table('users', function (Blueprint $table) {
            $table->string('share_account', 32)->nullable()->unique()->after('role');
            // When the player confirmed that a listing's text goes into a public index with history.
            $table->timestamp('share_consented_at')->nullable()->after('share_account');
        });

        // One listing on this host: a vehicle or a save, with the listing text it was published with.
        Schema::create('shares', function (Blueprint $table) {
            $table->id();
            $table->string('type', 16);
            $table->string('listing_id', 64)->unique();
            // Null once the owner deleted their account; the owner record keeps `account`.
            $table->foreignId('user_id')->nullable()->constrained()->nullOnDelete();
            $table->string('account', 32);
            $table->string('name');
            $table->json('authors');
            $table->string('abstract', 300);
            $table->text('description')->nullable();
            $table->string('license', 100);
            $table->json('tags');
            $table->json('dependencies');
            $table->string('pull_request_url')->nullable();
            $table->string('branch')->nullable();
            $table->timestamps();
        });

        // One release: one archive at a url that never changes (rule 25).
        Schema::create('share_releases', function (Blueprint $table) {
            $table->id();
            $table->foreignId('share_id')->constrained()->cascadeOnDelete();
            $table->string('version', 64);
            $table->string('status', 16);
            $table->string('path');
            $table->unsignedBigInteger('size');
            $table->char('sha256', 64);
            // install.folder: the name in the archive's meta.toml.
            $table->string('folder', 64);
            $table->string('game_version', 32);
            $table->json('dependencies');
            // A takedown keeps the row, so the version is never given out again (rule 11).
            $table->timestamp('taken_down_at')->nullable();
            $table->timestamps();

            $table->unique(['share_id', 'version']);
        });

        // An archive that passed or failed the checks and waits to be published, or to expire.
        Schema::create('share_uploads', function (Blueprint $table) {
            $table->ulid('id')->primary();
            $table->foreignId('user_id')->constrained()->cascadeOnDelete();
            $table->foreignId('share_id')->nullable()->constrained()->cascadeOnDelete();
            $table->string('type', 16)->nullable();
            $table->string('path');
            $table->unsignedBigInteger('size');
            $table->char('sha256', 64);
            $table->string('folder', 64)->nullable();
            $table->string('game_version', 32)->nullable();
            $table->json('dependencies');
            $table->json('checks');
            $table->boolean('passed');
            $table->string('client', 64)->nullable();
            $table->timestamp('expires_at');
            $table->timestamp('published_at')->nullable();
            $table->timestamps();
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('share_uploads');
        Schema::dropIfExists('share_releases');
        Schema::dropIfExists('shares');

        Schema::table('users', function (Blueprint $table) {
            $table->dropUnique(['share_account']);
            $table->dropColumn(['share_account', 'share_consented_at']);
        });
    }
};
