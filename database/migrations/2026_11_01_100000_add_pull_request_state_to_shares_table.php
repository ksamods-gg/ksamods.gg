<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Where the listing's pull request stands: open, merged or closed, from GitHub's webhook.
     */
    public function up(): void
    {
        Schema::table('shares', function (Blueprint $table) {
            $table->string('pull_request_state')->nullable()->after('pull_request_url');
        });
    }

    public function down(): void
    {
        Schema::table('shares', function (Blueprint $table) {
            $table->dropColumn('pull_request_state');
        });
    }
};
