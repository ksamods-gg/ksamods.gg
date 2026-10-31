<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * A release's Internet Archive mirror (rule 19), and when it entered the release list. The
     * entry waits for the mirror, because an entry is never edited afterwards (rule 10).
     */
    public function up(): void
    {
        Schema::table('share_releases', function (Blueprint $table) {
            $table->string('mirror_url')->nullable()->after('dependencies');
            $table->timestamp('listed_at')->nullable()->after('mirror_url');
        });

        // Every release before this one was listed when it was published.
        DB::table('share_releases')->update(['listed_at' => DB::raw('created_at')]);
    }

    public function down(): void
    {
        Schema::table('share_releases', function (Blueprint $table) {
            $table->dropColumn(['mirror_url', 'listed_at']);
        });
    }
};
