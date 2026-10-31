<?php

namespace App\Support\Share;

use Devium\Toml\Toml as TomlParser;
use DOMDocument;
use Throwable;
use ZipArchive;

/**
 * RFC 0085's checks of a vehicle or save archive, run before anything is written (rule 8):
 * meta.toml, the name rule, the root directory, the game file and `version`, plus the size limit
 * of the type and this host's own safety checks (paths, symlinks, zip bombs). The archive is never
 * unpacked; only meta.toml and the game file are read, each with a size cap.
 */
class ShareArchive
{
    /**
     * The file that marks each type, in the folder the game wrote (RFC 0085, The types).
     */
    public const GAME_FILES = ['vehicle' => 'vehicle.xml', 'save' => 'universe.xml'];

    // Limits the archive declares; the real bytes of the two files read are capped as well.
    private const MAX_ENTRIES = 10000;

    private const MAX_UNPACKED = 1024 * 1024 * 1024;

    private const MAX_DOCUMENT = 64 * 1024 * 1024;

    /**
     * @var list<array{name: string, passed: bool, message: string}>
     */
    public array $checks = [];

    /**
     * The content type, from the game file the archive holds.
     *
     * @var 'vehicle'|'save'|null
     */
    public ?string $type = null;

    /** The `name` in meta.toml, which becomes install.folder. */
    public ?string $folder = null;

    /** The game build that wrote the archive, from `version` in meta.toml. */
    public ?string $gameVersion = null;

    /**
     * The error code of rule 16 when a check failed: type_not_accepted, too_large or invalid_archive.
     */
    public ?string $error = null;

    /**
     * @param  string|null  $type  The listing's type, for a new version of an existing listing.
     */
    public static function inspect(string $file, int $size, ?string $type = null): self
    {
        $inspection = new self;

        try {
            $inspection->run($file, $size, $type);
        } catch (ArchiveRefused) {
            // The check that refused it is already in the list.
        }

        return $inspection;
    }

    public function passed(): bool
    {
        return $this->error === null;
    }

    private function run(string $file, int $size, ?string $listingType): void
    {
        $zip = new ZipArchive;

        if ($zip->open($file, ZipArchive::RDONLY) !== true) {
            $this->fail('archive', 'The file is not a zip archive.');
        }

        try {
            $entries = $this->entries($zip);
            $this->pass('archive', 'A zip archive with safe paths.');

            [$root, $type] = $this->root($entries);

            if ($listingType !== null && $type !== $listingType) {
                $this->fail('type', "This listing is a {$listingType}, and the archive holds a {$type}.");
            }

            if (! in_array($type, config('share.types'), true)) {
                $this->fail('type', "This host does not take a {$type}.", 'type_not_accepted');
            }

            $this->type = $type;
            $this->pass('type', "A {$type}, with its ".self::GAME_FILES[$type].'.');

            $limit = (int) config("share.max_bytes.{$type}");

            if ($size > $limit) {
                $this->fail('size', 'The archive is '.self::megabytes($size).", and a {$type} may be at most ".self::megabytes($limit).'.', 'too_large');
            }

            $this->pass('size', 'The archive is '.self::megabytes($size).'.');

            $this->checkMeta($zip, $root);
            $this->checkGameFile($zip, $root, self::GAME_FILES[$type]);

            // RFC 0085: the game writes a vehicle folder with only the two files.
            if ($type === 'vehicle') {
                $extra = array_filter($entries, fn (string $entry) => ! str_ends_with($entry, '/')
                    && ! in_array($entry, ["{$root}meta.toml", "{$root}vehicle.xml"], true));

                if ($extra !== []) {
                    $this->pass('other files', count($extra).' file(s) besides meta.toml and vehicle.xml, which install as they are.');
                }
            }
        } finally {
            $zip->close();
        }
    }

    /**
     * Every entry, after refusing unsafe paths, symlinks and zip bombs.
     *
     * @return list<string>
     */
    private function entries(ZipArchive $zip): array
    {
        if ($zip->numFiles === 0) {
            $this->fail('archive', 'The archive is empty.');
        }

        if ($zip->numFiles > self::MAX_ENTRIES) {
            $this->fail('archive', 'The archive holds too many files.');
        }

        $entries = [];
        $unpacked = 0;

        for ($i = 0; $i < $zip->numFiles; $i++) {
            $stat = $zip->statIndex($i);

            if ($stat === false) {
                $this->fail('archive', 'The archive is damaged.');
            }

            $entry = $stat['name'];
            $unpacked += $stat['size'];
            $segments = explode('/', rtrim($entry, '/'));

            if ($entry === '' || str_starts_with($entry, '/') || str_contains($entry, '\\') || str_contains($entry, "\0")
                || preg_match('/^[A-Za-z]:/', $entry) === 1 || array_intersect($segments, ['', '.', '..']) !== []) {
                $this->fail('archive', "The archive has an unsafe path: {$entry}");
            }

            $zip->getExternalAttributesIndex($i, $system, $attributes);

            if ($system === ZipArchive::OPSYS_UNIX && (($attributes >> 16) & 0170000) === 0120000) {
                $this->fail('archive', "The archive contains a symbolic link: {$entry}");
            }

            $entries[] = $entry;
        }

        if ($unpacked > self::MAX_UNPACKED) {
            $this->fail('archive', 'The archive unpacks to more than 1 GB.');
        }

        return $entries;
    }

    /**
     * The derived root (RFC 0085): the archive root when meta.toml and the game file are there, and
     * otherwise the one top-level directory that holds them. Returns the root as an entry prefix.
     *
     * @param  list<string>  $entries
     * @return array{string, 'vehicle'|'save'}
     */
    private function root(array $entries): array
    {
        $files = array_flip($entries);
        $candidates = [''];
        $tops = array_unique(array_map(fn (string $entry) => strtok($entry, '/'), array_filter($entries, fn (string $entry) => str_contains($entry, '/'))));

        if (count($tops) === 1) {
            $candidates[] = reset($tops).'/';
        }

        foreach ($candidates as $root) {
            $found = array_keys(array_filter(self::GAME_FILES, fn (string $game) => isset($files["{$root}{$game}"])));

            if (isset($files["{$root}meta.toml"]) && count($found) === 1) {
                $where = $root === '' ? 'at the top of the archive' : 'in the folder '.rtrim($root, '/');
                $this->pass('root', "meta.toml and the game file are {$where}.");

                return [$root, $found[0]];
            }

            if (isset($files["{$root}meta.toml"]) && count($found) > 1) {
                $this->fail('root', 'The archive holds both a vehicle.xml and a universe.xml, so it is neither a vehicle nor a save.');
            }
        }

        $this->fail('root', 'The archive needs meta.toml and vehicle.xml (a vehicle) or universe.xml (a save), at its top or in one folder.');
    }

    private function checkMeta(ZipArchive $zip, string $root): void
    {
        try {
            $meta = TomlParser::decode($this->read($zip, "{$root}meta.toml", 'meta.toml'), asArray: true);
        } catch (ArchiveRefused $refused) {
            throw $refused;
        } catch (Throwable) {
            $this->fail('meta.toml', 'meta.toml is not valid TOML, which stops the game from starting.');
        }

        if (! is_array($meta)) {
            $this->fail('meta.toml', 'meta.toml is not a TOML table.');
        }

        $this->pass('meta.toml', 'meta.toml parses.');

        $name = $meta['name'] ?? null;

        if (! is_string($name) || ! SaveName::isValid($name)) {
            $this->fail('name', 'The name in meta.toml must be 1 to 64 letters, digits, spaces, - or _, without spaces at either end or two together, and not a reserved name such as CON. Save it again in the game under such a name.');
        }

        $this->pass('name', "The name \"{$name}\" follows the game's name rule.");

        if ($root !== '' && rtrim($root, '/') !== $name) {
            $this->fail('root', 'The folder in the archive is "'.rtrim($root, '/')."\", and must be named after the name in meta.toml, \"{$name}\".");
        }

        $this->folder = $name;

        $version = $meta['version'] ?? null;

        // RFC 0017's full form, which the game shows, such as 2026.10.10.5554, with no suffix. The game
        // writes it with a leading v (VersionInfo.VersionString), which the index drops too (meta_game_min).
        if (is_string($version) && str_starts_with($version, 'v')) {
            $version = substr($version, 1);
        }

        if (! is_string($version) || preg_match('/^[0-9]{4}\.(?:1[0-2]|[1-9])\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$/', $version) !== 1) {
            $this->fail('version', 'The version in meta.toml must be the game build that wrote it, such as 2026.10.10.5554.');
        }

        $this->gameVersion = $version;
        $this->pass('version', "Written by game build {$version}, the lowest build a player needs.");
    }

    private function checkGameFile(ZipArchive $zip, string $root, string $game): void
    {
        $xml = new DOMDocument;
        $previous = libxml_use_internal_errors(true);
        // No network access and no entity expansion: the XML is untrusted.
        $parsed = $xml->loadXML($this->read($zip, "{$root}{$game}", $game), LIBXML_NONET);
        libxml_clear_errors();
        libxml_use_internal_errors($previous);

        if (! $parsed) {
            $this->fail('game file', "{$game} is not well-formed XML, so the game would skip or refuse it.");
        }

        $this->pass('game file', "{$game} is well-formed XML.");
    }

    /**
     * One entry, never more than MAX_DOCUMENT bytes whatever the archive declares.
     */
    private function read(ZipArchive $zip, string $entry, string $check): string
    {
        $stat = $zip->statName($entry);
        $stream = $stat !== false && $stat['size'] <= self::MAX_DOCUMENT ? $zip->getStream($entry) : false;
        $content = $stream !== false ? stream_get_contents($stream, self::MAX_DOCUMENT + 1) : false;

        if (is_resource($stream)) {
            fclose($stream);
        }

        if ($content === false || strlen($content) > self::MAX_DOCUMENT) {
            $this->fail($check, "{$check} is too large or could not be read.");
        }

        return $content;
    }

    private function pass(string $name, string $message): void
    {
        $this->checks[] = ['name' => $name, 'passed' => true, 'message' => $message];
    }

    /**
     * @throws ArchiveRefused
     */
    private function fail(string $name, string $message, string $error = 'invalid_archive'): never
    {
        $this->checks[] = ['name' => $name, 'passed' => false, 'message' => $message];
        $this->error = $error;

        throw new ArchiveRefused;
    }

    private static function megabytes(int $bytes): string
    {
        return round($bytes / 1024 / 1024, 1).' MB';
    }
}
