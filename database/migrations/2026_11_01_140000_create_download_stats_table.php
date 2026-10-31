<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Each listing's total downloads, once a day, since the index publishes only the total.
     */
    public function up(): void
    {
        Schema::create('download_stats', function (Blueprint $table) {
            $table->string('listing_id');
            $table->date('date');
            $table->unsignedBigInteger('downloads');
            $table->primary(['listing_id', 'date']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('download_stats');
    }
};
