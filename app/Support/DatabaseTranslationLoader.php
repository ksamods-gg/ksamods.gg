<?php

namespace App\Support;

use Illuminate\Translation\FileLoader;

/**
 * Laravel's translation loader, with the community's live translations as the JSON strings of
 * each language, so __('English text') on the server shows them.
 */
class DatabaseTranslationLoader extends FileLoader
{
    /**
     * @return array<string, mixed>
     */
    public function load($locale, $group, $namespace = null): array
    {
        $lines = parent::load($locale, $group, $namespace);

        if ($group === '*' && $namespace === '*') {
            return [...$lines, ...Translations::live($locale)];
        }

        return $lines;
    }
}
