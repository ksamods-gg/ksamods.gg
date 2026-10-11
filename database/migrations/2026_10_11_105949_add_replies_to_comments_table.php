<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        Schema::table('comments', function (Blueprint $table) {
            // A reply belongs to a top-level comment or to a review, never both.
            $table->foreignId('parent_id')->nullable()->after('listing_id')->constrained('comments')->cascadeOnDelete();
            $table->foreignId('review_id')->nullable()->after('parent_id')->constrained()->cascadeOnDelete();
            // A deleted comment that has replies stays as a placeholder, so the replies keep their context.
            $table->softDeletes();
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::table('comments', function (Blueprint $table) {
            $table->dropConstrainedForeignId('parent_id');
            $table->dropConstrainedForeignId('review_id');
            $table->dropSoftDeletes();
        });
    }
};
