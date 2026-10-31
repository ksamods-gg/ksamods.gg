<?php

use App\Enums\Role;
use App\Models\Language;
use App\Models\Translation;
use App\Models\User;
use App\Support\Translations;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

beforeEach(fn () => $this->withoutVite());

it('finds the site\'s texts in its source', function () {
    expect(Translations::catalog())
        ->toContain('Help translate')
        ->toContain('Last updated :date')
        ->toContain('Keep :placeholder in the translation; the site fills it in.');
});

it('lets a player request a language, which an admin turns on', function () {
    $player = User::factory()->create();

    $this->actingAs($player)->post('/translate', ['code' => 'de', 'name' => 'German', 'native_name' => 'Deutsch'])
        ->assertRedirect('/translate/de');
    $this->actingAs($player)->post('/translate', ['code' => 'en', 'name' => 'English', 'native_name' => 'English'])
        ->assertSessionHasErrors('code');
    expect(Language::sole()->active)->toBeFalse();

    $this->actingAs($player)->post('/languages/de/approve')->assertForbidden();
    $this->actingAs(User::factory()->create(['role' => Role::Admin]))->post('/languages/de/approve');

    expect(Language::sole()->active)->toBeTrue();
});

it('shows a suggestion once a second player votes for it, and keeps placeholders', function () {
    Language::create(['code' => 'de', 'name' => 'German', 'native_name' => 'Deutsch', 'active' => true]);
    [$author, $voter, $other] = User::factory()->count(3)->create();

    $this->actingAs($author)->post('/translate/de', ['source' => 'Last updated :date', 'text' => 'Zuletzt geändert'])
        ->assertSessionHasErrors('text');
    $this->actingAs($author)->post('/translate/de', ['source' => 'Last updated :date', 'text' => 'Zuletzt geändert am :date']);
    $this->actingAs($author)->post('/translate/de', ['source' => 'Not a text of the site', 'text' => 'X'])->assertSessionHasErrors('source');

    $translation = Translation::sole();
    expect(Translations::live('de'))->toBe([]);

    // The author cannot vote for their own suggestion; another player can.
    $this->actingAs($author)->post("/translations/{$translation->id}/vote", ['value' => 1])->assertForbidden();
    $this->actingAs($voter)->post("/translations/{$translation->id}/vote", ['value' => 1]);

    expect(Translations::live('de'))->toBe(['Last updated :date' => 'Zuletzt geändert am :date'])
        ->and(__('Last updated :date', ['date' => '1. November'], 'de'))->toBe('Zuletzt geändert am 1. November');

    // A vote against takes it off again.
    $this->actingAs($other)->post("/translations/{$translation->id}/vote", ['value' => -1]);
    expect(Translations::live('de'))->toBe([]);
});

it('shows the page in the chosen language, with its translations', function () {
    Language::create(['code' => 'de', 'name' => 'German', 'native_name' => 'Deutsch', 'active' => true]);
    $translation = Translation::create(['language_id' => Language::sole()->id, 'source' => 'Help translate', 'source_hash' => sha1('Help translate'), 'text' => 'Hilf beim Übersetzen']);
    DB::table('translation_votes')->insert(['translation_id' => $translation->id, 'user_id' => User::factory()->create()->id, 'value' => 1]);

    $this->post('/locale', ['locale' => 'de'])->assertCookie('locale');

    $this->withCookie('locale', 'de')->get('/translate')->assertInertia(fn (Assert $page) => $page
        ->where('locale', 'de')
        ->where('translations.Help translate', 'Hilf beim Übersetzen')
        ->where('languages.0.code', 'de'));

    // A browser that prefers German gets it without choosing.
    $this->withHeader('Accept-Language', 'de-DE,de;q=0.9')->get('/translate')->assertInertia(fn (Assert $page) => $page->where('locale', 'de'));
});
