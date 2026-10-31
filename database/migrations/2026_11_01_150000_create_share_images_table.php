<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Screenshots of a vehicle or save, as WebP on the share disk, with the facts RFC 0058's image
     * records name. The first is the cover; its square crop is the listing's icon.
     */
    public function up(): void
    {
        Schema::create('share_images', function (Blueprint $table) {
            $table->id();
            $table->foreignId('share_id')->constrained()->cascadeOnDelete();
            $table->unsignedTinyInteger('position');
            $table->string('path');
            $table->char('sha256', 64);
            $table->unsignedSmallInteger('width');
            $table->unsignedSmallInteger('height');
            $table->unsignedInteger('size');
            $table->timestamps();
        });

        Schema::table('shares', function (Blueprint $table) {
            // The cover's square crop, the icon record of the listing document.
            $table->json('icon')->nullable()->after('tags');
        });
    }

    public function down(): void
    {
        Schema::table('shares', function (Blueprint $table) {
            $table->dropColumn('icon');
        });
        Schema::dropIfExists('share_images');
    }
};
