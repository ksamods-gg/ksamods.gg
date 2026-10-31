<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Community translations: languages players request and admins approve, suggested
     * translations of each English text of the site, and the votes that choose between them.
     */
    public function up(): void
    {
        Schema::create('languages', function (Blueprint $table) {
            $table->id();
            // A BCP 47 tag, such as de or pt-BR.
            $table->string('code', 16)->unique();
            $table->string('name');
            $table->string('native_name');
            $table->boolean('active')->default(false);
            $table->foreignId('requested_by')->nullable()->constrained('users')->nullOnDelete();
            $table->timestamps();
        });

        Schema::create('translations', function (Blueprint $table) {
            $table->id();
            $table->foreignId('language_id')->constrained()->cascadeOnDelete();
            // The English text it translates, which is also its key.
            $table->text('source');
            $table->char('source_hash', 40)->index();
            $table->text('text');
            $table->foreignId('user_id')->nullable()->constrained()->nullOnDelete();
            $table->timestamps();
            $table->index(['language_id', 'source_hash']);
        });

        Schema::create('translation_votes', function (Blueprint $table) {
            $table->foreignId('translation_id')->constrained()->cascadeOnDelete();
            $table->foreignId('user_id')->constrained()->cascadeOnDelete();
            // +1 or -1.
            $table->tinyInteger('value');
            $table->primary(['translation_id', 'user_id']);
        });

        Schema::table('users', function (Blueprint $table) {
            $table->string('locale', 16)->nullable();
        });
    }

    public function down(): void
    {
        Schema::table('users', function (Blueprint $table) {
            $table->dropColumn('locale');
        });
        Schema::dropIfExists('translation_votes');
        Schema::dropIfExists('translations');
        Schema::dropIfExists('languages');
    }
};
