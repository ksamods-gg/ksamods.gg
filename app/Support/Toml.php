<?php

namespace App\Support;

/**
 * Writes the TOML subset a listing needs: strings, integers, booleans, string lists, tables and lists of tables.
 */
class Toml
{
    /**
     * @param  array<string, mixed>  $data
     */
    public static function encode(array $data, string $table = ''): string
    {
        $lines = [];
        $tables = [];

        foreach ($data as $key => $value) {
            if ($value === null || $value === []) {
                continue;
            }

            if (is_array($value) && (! array_is_list($value) || is_array($value[0]))) {
                $tables[$key] = $value;
            } else {
                $lines[] = $key.' = '.self::value($value);
            }
        }

        $toml = $lines === [] ? '' : implode("\n", $lines)."\n";

        foreach ($tables as $key => $value) {
            $name = $table === '' ? $key : "{$table}.{$key}";

            // A list of tables, such as [[images.description]].
            foreach (array_is_list($value) ? $value : [] as $item) {
                $toml .= "\n[[{$name}]]\n".self::encode($item, $name);
            }

            if (! array_is_list($value)) {
                $toml .= "\n[{$name}]\n".self::encode($value, $name);
            }
        }

        return $toml;
    }

    private static function value(mixed $value): string
    {
        return match (true) {
            is_int($value) => (string) $value,
            is_bool($value) => $value ? 'true' : 'false',
            is_array($value) => '['.implode(', ', array_map(self::value(...), $value)).']',
            // Multi-line text reads best as a literal block, which allows no escapes at all.
            is_string($value) && str_contains($value, "\n") && ! str_contains($value, "'''")
                && ! preg_match('/[\x00-\x08\x0B-\x1F\x7F]/', $value) => "'''\n{$value}'''",
            // A JSON string without escaped slashes is a valid TOML basic string.
            default => json_encode($value, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR),
        };
    }
}
