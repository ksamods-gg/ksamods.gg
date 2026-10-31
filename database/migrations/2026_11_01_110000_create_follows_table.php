<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Mods, vehicles and saves a user follows for new releases; the newest version of each
     * followed mod the site has seen, to tell a new one; and the user's email digest.
     */
    public function up(): void
    {
        Schema::create('follows', function (Blueprint $table) {
            $table->id();
            $table->foreignId('user_id')->constrained()->cascadeOnDelete();
            // mod, vehicle or save, with its listing id.
            $table->string('kind', 16);
            $table->string('listing_id');
            $table->timestamps();
            $table->unique(['user_id', 'kind', 'listing_id']);
            $table->index(['kind', 'listing_id']);
        });

        Schema::create('seen_releases', function (Blueprint $table) {
            $table->string('listing_id')->primary();
            $table->string('version');
            $table->timestamps();
        });

        Schema::table('users', function (Blueprint $table) {
            $table->string('digest_email')->nullable();
            $table->timestamp('digest_email_verified_at')->nullable();
            // off, daily, weekly or monthly.
            $table->string('digest_frequency', 16)->default('off');
            $table->timestamp('digest_sent_at')->nullable();
        });
    }

    public function down(): void
    {
        Schema::table('users', function (Blueprint $table) {
            $table->dropColumn(['digest_email', 'digest_email_verified_at', 'digest_frequency', 'digest_sent_at']);
        });
        Schema::dropIfExists('seen_releases');
        Schema::dropIfExists('follows');
    }
};
