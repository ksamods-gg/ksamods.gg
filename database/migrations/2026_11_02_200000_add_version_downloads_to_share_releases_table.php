<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('share_releases', function (Blueprint $table) {
            // Downloads of this version; the listing's total stays on shares.downloads.
            $table->unsignedInteger('downloads')->default(0);
        });

        Schema::table('shares', function (Blueprint $table) {
            // The total the release list last published, so the daily publish rewrites only changed lists.
            $table->unsignedInteger('published_downloads')->default(0);
        });

        // Downloads counted before versions were: each listing's newest release still up takes them.
        DB::table('shares')->where('downloads', '>', 0)->orderBy('id')->each(function (object $share) {
            $release = DB::table('share_releases')->where('share_id', $share->id)->whereNull('taken_down_at')->latest('id')->first();

            if ($release !== null) {
                DB::table('share_releases')->where('id', $release->id)->update(['downloads' => $share->downloads]);
            }
        });
    }

    public function down(): void
    {
        Schema::table('share_releases', function (Blueprint $table) {
            $table->dropColumn('downloads');
        });

        Schema::table('shares', function (Blueprint $table) {
            $table->dropColumn('published_downloads');
        });
    }
};
