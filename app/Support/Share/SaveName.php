<?php

namespace App\Support\Share;

/**
 * The game's name rule for a vehicle or save folder (RFC 0085), as SaveName.IsSanitized applies it:
 *
 * - 1 to 64 UTF-16 code units, each a letter (Lu, Ll, Lt, Lm, Lo), a decimal digit (Nd), a space,
 *   "-" or "_". So a character outside the Basic Multilingual Plane, two surrogates, fails.
 * - No space at the start or end, and never two together.
 * - Not a reserved Windows device name, in any case.
 *
 * The cases live in tests/fixtures/save-names.json, a copy of content-index's schemas/save-names.json.
 */
class SaveName
{
    public static function isValid(string $name): bool
    {
        if (! mb_check_encoding($name, 'UTF-8')) {
            return false;
        }

        $units = unpack('n*', mb_convert_encoding($name, 'UTF-16BE', 'UTF-8')) ?: [];

        if ($units === [] || count($units) > 64) {
            return false;
        }

        foreach ($units as $unit) {
            // A surrogate is half of a character outside the BMP, never a letter by itself.
            if ($unit >= 0xD800 && $unit <= 0xDFFF) {
                return false;
            }

            if (preg_match('/^[\p{Lu}\p{Ll}\p{Lt}\p{Lm}\p{Lo}\p{Nd} _-]$/u', mb_chr($unit, 'UTF-8')) !== 1) {
                return false;
            }
        }

        return $name === trim($name, ' ')
            && ! str_contains($name, '  ')
            && preg_match('/^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$/i', $name) !== 1;
    }
}
