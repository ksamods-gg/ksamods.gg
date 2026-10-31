<?php

namespace App\Support;

use App\Models\Language;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\DB;
use Symfony\Component\Finder\Finder;
use Throwable;

/**
 * The site's community translations. Every English text the site shows is its own key, written as
 * t('…') in the frontend and __('…') on the server; the catalog of them comes from scanning the
 * source. Players suggest translations and vote on them: a suggestion counts its author's vote
 * plus the others, and the best one for a text goes live once it scores at least THRESHOLD.
 */
class Translations
{
    public const THRESHOLD = 2;

    /**
     * Every English text the site can show, from t(), $t() and __() calls in its source.
     *
     * @return list<string>
     */
    public static function catalog(): array
    {
        $finder = Finder::create()->files()
            ->in([resource_path('js'), resource_path('views'), app_path()])
            ->name(['*.vue', '*.ts', '*.php'])
            ->notName(['*.test.ts', '*.d.ts']);
        $files = iterator_to_array($finder, false);

        // Cached until a file changes, which in production is a deploy.
        $version = md5(implode(',', array_map(fn ($file) => $file->getMTime(), $files)));

        return Cache::rememberForever("translations:catalog:{$version}", function () use ($files) {
            $texts = [];

            foreach ($files as $file) {
                preg_match_all('/(?<![\w$.>])(?:\$t|t|__)\(\s*([\'"])((?:\\\\.|(?!\1)[^\\\\])*)\1\s*[,)]/s', $file->getContents(), $matches, PREG_SET_ORDER);

                foreach ($matches as [, $quote, $text]) {
                    $text = str_replace(['\\'.$quote, '\\\\'], [$quote, '\\'], $text);

                    if (trim($text) !== '' && ! str_contains($text, '${')) {
                        $texts[$text] = true;
                    }
                }
            }

            $texts = array_map('strval', array_keys($texts));
            sort($texts);

            return $texts;
        });
    }

    /**
     * The translation the site shows for each English text in a language: the best-scoring
     * suggestion that reached the threshold, the earlier on a tie.
     *
     * @return array<string, string>
     */
    public static function live(string $locale): array
    {
        if ($locale === 'en') {
            return [];
        }

        return Cache::rememberForever("translations:live:{$locale}", function () use ($locale) {
            try {
                $rows = DB::table('translations')
                    ->join('languages', 'languages.id', '=', 'translations.language_id')
                    ->leftJoin('translation_votes', 'translation_votes.translation_id', '=', 'translations.id')
                    ->where('languages.code', $locale)
                    ->groupBy('translations.id', 'translations.source', 'translations.text')
                    ->selectRaw('translations.id, translations.source, translations.text, 1 + COALESCE(SUM(translation_votes.value), 0) as score')
                    ->havingRaw('score >= ?', [self::THRESHOLD])
                    ->orderByDesc('score')
                    ->orderBy('translations.id')
                    ->get();
            } catch (Throwable) {
                // Before the migrations ran, there is nothing to translate with.
                return [];
            }

            $live = [];

            foreach ($rows as $row) {
                $live[$row->source] ??= $row->text;
            }

            return $live;
        });
    }

    /**
     * Shares of the catalog each language translates, from 0 to 100.
     *
     * @return array<string, int>
     */
    public static function progress(): array
    {
        return Cache::remember('translations:progress', 600, function () {
            $catalog = array_flip(self::catalog());

            return Language::pluck('code')->mapWithKeys(fn (string $code) => [
                $code => $catalog === [] ? 0 : (int) floor(count(array_intersect_key(self::live($code), $catalog)) / count($catalog) * 100),
            ])->all();
        });
    }

    /**
     * After a suggestion or a vote, the language's live translations are worked out again.
     */
    public static function forget(string $locale): void
    {
        Cache::forget("translations:live:{$locale}");
        Cache::forget('translations:progress');
    }

    /**
     * The active languages, for the language switcher.
     *
     * @return list<array{code: string, name: string, native_name: string, progress: int}>
     */
    public static function languages(): array
    {
        try {
            $progress = self::progress();

            return array_values(Language::where('active', true)->orderBy('native_name')->get()->map(fn (Language $language) => [
                'code' => $language->code,
                'name' => $language->name,
                'native_name' => $language->native_name,
                'progress' => $progress[$language->code] ?? 0,
            ])->all());
        } catch (Throwable) {
            return [];
        }
    }
}
