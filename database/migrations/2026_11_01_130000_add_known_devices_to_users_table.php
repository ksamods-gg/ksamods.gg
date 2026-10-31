<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Fingerprints of the browsers a user signed in from, to tell them about a sign-in from a new one.
     */
    public function up(): void
    {
        Schema::table('users', function (Blueprint $table) {
            $table->json('known_devices')->nullable();
        });
    }

    public function down(): void
    {
        Schema::table('users', function (Blueprint $table) {
            $table->dropColumn('known_devices');
        });
    }
};
