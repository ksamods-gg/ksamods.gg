<?php

namespace App\Http\Controllers;

use App\Models\Language;
use App\Models\Translation;
use App\Support\Translations;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Cookie;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\Rule;
use Illuminate\Validation\ValidationException;
use Inertia\Inertia;
use Inertia\Response;

/**
 * The community translation of the site: languages, the texts to translate, suggestions and votes.
 */
class TranslationController extends Controller
{
    private const PER_PAGE = 30;

    public function index(Request $request): Response
    {
        $progress = Translations::progress();

        return Inertia::render('Translate/Index', [
            'languages' => Language::orderByDesc('active')->orderBy('name')->get()->map(fn (Language $language) => [
                'code' => $language->code,
                'name' => $language->name,
                'native_name' => $language->native_name,
                'active' => $language->active,
                'progress' => $progress[$language->code] ?? 0,
            ]),
            'texts' => count(Translations::catalog()),
            'threshold' => Translations::THRESHOLD,
            'canApprove' => $request->user()?->isAdmin() ?? false,
        ]);
    }

    /**
     * A player asks for a language; it can be translated at once, and shows once an admin approves it.
     */
    public function requestLanguage(Request $request): RedirectResponse
    {
        $validated = $request->validate([
            'code' => ['required', 'string', 'regex:/^[a-z]{2,3}(?:-(?:[A-Z]{2}|[A-Z][a-z]{3}))?$/', 'not_in:en', Rule::unique('languages', 'code')],
            'name' => ['required', 'string', 'max:60'],
            'native_name' => ['required', 'string', 'max:60'],
        ], ['code.regex' => __('A language code such as de, pt-BR or zh-Hant.')]);

        Language::create([...$validated, 'requested_by' => $request->user()->id]);
        Inertia::flash('status', __('Thanks! You can start translating now; an admin turns the language on for everyone.'));

        return to_route('translate.show', $validated['code']);
    }

    public function approve(Language $language): RedirectResponse
    {
        $language->forceFill(['active' => true])->save();
        Translations::forget($language->code);

        return back();
    }

    /**
     * Turns a language off again, or drops a request, with its suggestions.
     */
    public function destroyLanguage(Language $language): RedirectResponse
    {
        $language->delete();
        Translations::forget($language->code);

        return to_route('translate.index');
    }

    public function show(Request $request, string $code): Response
    {
        $language = Language::where('code', $code)->firstOrFail();
        $search = trim((string) $request->query('q'));
        $filter = $request->query('show') === 'all' ? 'all' : 'untranslated';
        $live = Translations::live($code);

        $texts = collect(Translations::catalog())
            ->when($search !== '', fn ($texts) => $texts->filter(fn (string $text) => mb_stripos($text, $search) !== false || mb_stripos($live[$text] ?? '', $search) !== false))
            ->when($filter === 'untranslated', fn ($texts) => $texts->reject(fn (string $text) => isset($live[$text])))
            ->values();
        $page = max(1, (int) $request->query('page', 1));
        $shown = $texts->slice(($page - 1) * self::PER_PAGE, self::PER_PAGE)->values();
        $user = $request->user();

        // The suggestions for the texts on this page, with their scores and the player's vote.
        $suggestions = Translation::with('user:id,name')
            ->where('language_id', $language->id)
            ->whereIn('source_hash', $shown->map(fn (string $text) => sha1($text)))
            ->withSum('votes as votes_total', 'value')
            ->get()
            ->groupBy('source');
        $mine = $user === null ? collect() : DB::table('translation_votes')->where('user_id', $user->id)
            ->whereIn('translation_id', $suggestions->flatten()->pluck('id'))->pluck('value', 'translation_id');

        return Inertia::render('Translate/Show', [
            'language' => ['code' => $language->code, 'name' => $language->name, 'native_name' => $language->native_name, 'active' => $language->active],
            'search' => $search,
            'show' => $filter,
            'page' => $page,
            'pages' => max(1, (int) ceil($texts->count() / self::PER_PAGE)),
            'total' => $texts->count(),
            'threshold' => Translations::THRESHOLD,
            'texts' => $shown->map(fn (string $text) => [
                'source' => $text,
                'live' => $live[$text] ?? null,
                'suggestions' => ($suggestions[$text] ?? collect())
                    ->map(fn (Translation $translation) => [
                        'id' => $translation->id,
                        'text' => $translation->text,
                        'score' => 1 + (int) $translation->votes_total,
                        'author' => $translation->user?->name,
                        'mine' => $user !== null && $translation->user_id === $user->id,
                        'vote' => (int) ($mine[$translation->id] ?? 0),
                        'canDelete' => $user !== null && ($translation->user_id === $user->id || $user->canModerate()),
                    ])
                    ->sortByDesc('score')
                    ->values(),
            ]),
        ]);
    }

    public function suggest(Request $request, string $code): RedirectResponse
    {
        $language = Language::where('code', $code)->firstOrFail();
        $validated = $request->validate([
            'source' => ['required', 'string', Rule::in(Translations::catalog())],
            'text' => ['required', 'string', 'max:5000'],
        ]);

        // A placeholder such as :name must survive, or the page would show the wrong thing.
        preg_match_all('/:[a-z_]+/i', $validated['source'], $placeholders);

        foreach (array_unique($placeholders[0]) as $placeholder) {
            if (! str_contains($validated['text'], $placeholder)) {
                throw ValidationException::withMessages(['text' => __('Keep :placeholder in the translation; the site fills it in.', ['placeholder' => $placeholder])]);
            }
        }

        $exists = Translation::where('language_id', $language->id)->where('source_hash', sha1($validated['source']))
            ->where('text', trim($validated['text']))->exists();

        if (! $exists) {
            Translation::create([
                'language_id' => $language->id,
                'source' => $validated['source'],
                'source_hash' => sha1($validated['source']),
                'text' => trim($validated['text']),
                'user_id' => $request->user()->id,
            ]);
            Translations::forget($code);
        }

        return back();
    }

    /**
     * Up, down, or back to no vote. A suggestion's author already counts for it.
     */
    public function vote(Request $request, Translation $translation): RedirectResponse
    {
        $validated = $request->validate(['value' => ['required', 'integer', 'in:-1,0,1']]);
        abort_if($translation->user_id === $request->user()->id, 403);

        $vote = DB::table('translation_votes')->where('translation_id', $translation->id)->where('user_id', $request->user()->id);
        $validated['value'] === 0
            ? $vote->delete()
            : DB::table('translation_votes')->upsert(
                [['translation_id' => $translation->id, 'user_id' => $request->user()->id, 'value' => $validated['value']]],
                ['translation_id', 'user_id'],
                ['value'],
            );
        Translations::forget($translation->language->code);

        return back();
    }

    public function destroy(Request $request, Translation $translation): RedirectResponse
    {
        abort_unless($translation->user_id === $request->user()->id || $request->user()->canModerate(), 403);
        $translation->delete();
        Translations::forget($translation->language->code);

        return back();
    }

    /**
     * Switches the site's language: for the account when signed in, and in a cookie either way.
     */
    public function switch(Request $request): RedirectResponse
    {
        $validated = $request->validate(['locale' => ['required', 'string', Rule::in(['en', ...array_column(Translations::languages(), 'code')])]]);
        $request->user()?->forceFill(['locale' => $validated['locale']])->save();
        Cookie::queue('locale', $validated['locale'], 60 * 24 * 365);

        return back();
    }
}
