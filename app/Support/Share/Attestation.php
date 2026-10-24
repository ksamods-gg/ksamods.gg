<?php

namespace App\Support\Share;

/**
 * The canonical form of a pull request's change, which an attestation is named after (RFC 0094,
 * The ownership proof): one line per path, `<path> <blob>` or `<path> deleted`, sorted by the
 * bytes of the path, each ending in LF. Checked against tests/fixtures/attestation-vectors.json,
 * a copy of content-index's schemas/attestation-vectors.json.
 */
class Attestation
{
    /**
     * @param  array<string, string|null>  $blobs  Each path the change touches, with its git blob id, or null when deleted.
     */
    public static function canonical(array $blobs): string
    {
        ksort($blobs, SORT_STRING);

        return implode('', array_map(fn (string $path, ?string $blob) => $path.' '.($blob ?? 'deleted')."\n", array_keys($blobs), $blobs));
    }

    /**
     * The digest of a change given as the content of each file, as the host writes it.
     *
     * @param  array<string, string|null>  $files  Each path with its new content, or null when deleted.
     */
    public static function digest(array $files): string
    {
        return hash('sha256', self::canonical(array_map(fn (?string $content) => $content === null ? null : self::blob($content), $files)));
    }

    /**
     * The git blob id of a file, as the pull request files API reports it.
     */
    public static function blob(string $content): string
    {
        return sha1('blob '.strlen($content)."\0".$content);
    }
}
