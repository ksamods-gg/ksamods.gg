<?php

namespace App\Support;

/**
 * The member rules of a mod pack (RFC 0080), ported from site/js/pack.js in the content index so
 * a pack fails here, in the same words, before it fails there. schemas/pack-vectors.json holds
 * the cases both run; tests/Feature/PackRulesTest.php runs them against this class. Only
 * [[mods]]: the index takes no vehicles or saves in a pack yet.
 *
 * @phpstan-type Pin array{id: string, version: string}
 * @phpstan-type Option array{id: string, min?: string, max?: string}
 * @phpstan-type Member array{id: string, type: string, delisted: bool, disputed: bool, releases: array<string, array<string, mixed>>}
 */
class PackRules
{
    // The schema's SemVer, which decides that a pin names a release at all.
    private const PIN_VERSION = '/^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*)?(?:\+[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*)?$/D';

    /**
     * Listings by casefolded id, and the casefolded ids that packs hold.
     *
     * @param  array<string, Member>  $listed
     * @param  array<string, true>  $packs
     */
    private function __construct(private array $listed, private array $packs) {}

    /**
     * From a snapshot in the shape of the vectors: listings with id, type and releases, and the
     * ids that are delisted or disputed.
     *
     * @param  array<string, mixed>  $snapshot
     */
    public static function fromSnapshot(array $snapshot): self
    {
        $listed = [];
        $delisted = array_map('strtolower', (array) ($snapshot['delisted'] ?? []));
        $disputed = array_map('strtolower', (array) ($snapshot['disputed'] ?? []));

        foreach ((array) ($snapshot['listings'] ?? []) as $listing) {
            $key = strtolower((string) $listing['id']);
            $releases = [];

            foreach ($listing['releases'] ?? [] as $release) {
                $releases[(string) $release['version']] = $release;
            }

            // Newest first, as the picker offers them.
            uksort($releases, fn ($a, $b) => self::compare((string) $b, (string) $a) ?? 0);
            $listed[$key] = [
                'id' => (string) $listing['id'],
                'type' => (string) ($listing['type'] ?? 'mod'),
                'delisted' => in_array($key, $delisted, true),
                'disputed' => in_array($key, $disputed, true),
                'releases' => $releases,
            ];
        }

        $packs = [];

        foreach ((array) ($snapshot['packs'] ?? []) as $pack) {
            $packs[strtolower((string) $pack['id'])] = true;
        }

        return new self($listed, $packs);
    }

    /**
     * From the content index this site reads.
     */
    public static function fromIndex(): self
    {
        $snapshot = ContentIndex::snapshot();

        return self::fromSnapshot([
            'listings' => array_map(fn (array $listing) => [
                'id' => $listing['id'],
                'type' => $listing['authored']['type'],
                'releases' => $listing['releases'] ?? [],
            ], $snapshot['listings']),
            'packs' => $snapshot['packs'],
        ]);
    }

    /**
     * The errors and notes of the pinned mods, each as "mods[n]: why", as memberMessages.
     *
     * @param  list<mixed>  $mods
     * @return array{errors: list<string>, notes: list<string>}
     */
    public function check(array $mods): array
    {
        $errors = [];
        $notes = [];
        $stamped = [];

        foreach ($mods as $number => $pin) {
            if (! is_array($pin)) {
                continue;
            }

            [$problem, $release] = $this->pinProblem($pin);

            if ($problem !== null) {
                $errors[] = "mods[{$number}]: {$problem}";
            } elseif ($release !== null) {
                $stamped[] = [$pin, $release, $number];
            }
        }

        $pinned = $this->pinnedMap($mods);

        foreach ($stamped as [$pin, $release, $number]) {
            foreach ($this->incomplete($pin, $release, $pinned) as $text) {
                $errors[] = "mods[{$number}]: {$text}";
            }
        }

        foreach ($mods as $number => $pin) {
            $listing = is_array($pin) && is_string($pin['id'] ?? null) ? ($this->listed[strtolower($pin['id'])] ?? null) : null;

            if ($listing !== null && $listing['type'] === 'mod' && $listing['disputed']) {
                $notes[] = "mods[{$number}]: '{$pin['id']}' is disputed, and a client warns about it";
            }
        }

        return ['errors' => $errors, 'notes' => $notes];
    }

    /**
     * The pins "Add the missing dependencies" adds: each required dependency no pin names, at
     * the newest stable release inside the bounds of every pinned release that requires it. What
     * it cannot add, such as an any_of, stays a note, as missingDependencies.
     *
     * @param  list<mixed>  $mods
     * @return array{pins: list<Pin>, notes: list<string>}
     */
    public function missing(array $mods): array
    {
        $authored = array_values(array_filter($mods, 'is_array'));
        $written = [];

        foreach ($authored as $pin) {
            if (is_string($pin['id'] ?? null)) {
                $written[strtolower($pin['id'])] = true;
            }
        }

        $added = [];
        $requirements = function () use (&$added, $authored): array {
            $found = [];

            foreach ([...$authored, ...$added] as $pin) {
                if (! is_string($pin['id'] ?? null) || ! is_string($pin['version'] ?? null)) {
                    continue;
                }

                foreach ($this->required($this->stamped($pin['id'], $pin['version'])) as $dependency) {
                    $options = $this->options($dependency);

                    if ($options !== []) {
                        $found[] = [$pin, $options];
                    }
                }
            }

            return $found;
        };

        // Each round chooses every added pin again from the needs of all pins, because a release
        // added later can narrow the bounds of a mod added earlier.
        for ($round = 0; $round <= count($this->listed); $round++) {
            $needed = [];

            foreach ($requirements() as [, $options]) {
                $key = count($options) === 1 ? strtolower($options[0]['id']) : null;

                if ($key === null || isset($written[$key]) || ! $this->isMod($key)) {
                    continue;
                }

                $needed[$key][] = $options[0];
            }

            $next = [];

            foreach ($needed as $key => $needs) {
                $member = $this->listed[$key];

                foreach ($member['releases'] as $version => $release) {
                    $version = (string) $version;

                    if (($release['release_status'] ?? null) !== 'stable' || ! empty($release['yanked'])) {
                        continue;
                    }

                    $candidate = [$key => ['id' => $member['id'], 'version' => $version]];

                    if (array_all($needs, fn (array $option) => $this->pinnedWithin($option, $candidate))) {
                        $next[] = ['id' => $member['id'], 'version' => $version];

                        break;
                    }
                }
            }

            if ($next == $added) {
                break;
            }

            $added = $next;
        }

        $ids = [];

        foreach ([...$authored, ...$added] as $pin) {
            if (is_string($pin['id'] ?? null)) {
                $ids[strtolower($pin['id'])] = true;
            }
        }

        $notes = [];

        foreach ($requirements() as [$pin, $options]) {
            if (array_any($options, fn (array $option) => isset($ids[strtolower($option['id'])]))) {
                continue;
            }

            $needs = $this->named($pin).' requires '.$this->wanted($options);
            $notes[] = match (true) {
                ! array_any($options, fn (array $option) => $this->isMod(strtolower($option['id']))) => "{$needs}, and no listed mod meets this, so no pack can pin this release",
                count($options) > 1 => "{$needs}, so pin the one you want",
                default => "{$needs}, and no stable release of it is inside the bounds of every member that requires it",
            };
        }

        return ['pins' => $added, 'notes' => $notes];
    }

    /**
     * The newest game build any pinned release needs, which a pack's game_min should be at least,
     * as proposedGameMin.
     *
     * @param  list<Pin>  $mods
     */
    public function gameMin(array $mods): ?string
    {
        $highest = null;

        foreach ($mods as $pin) {
            $release = $this->listed[strtolower($pin['id'])]['releases'][$pin['version']] ?? null;

            if (isset($release['game_min'], $release['game_min_revision'])
                && ($highest === null || $release['game_min_revision'] > $highest['game_min_revision'])) {
                $highest = $release;
            }
        }

        return $highest['game_min'] ?? null;
    }

    /**
     * Compares two SemVer versions as the index does; a missing number reads as 0 (RFC 0072).
     * Null when either does not parse.
     */
    public static function compare(?string $left, ?string $right): ?int
    {
        $a = self::semver($left);
        $b = self::semver($right);

        if ($a === null || $b === null) {
            return null;
        }

        for ($i = 0; $i < 3; $i++) {
            if ($a['core'][$i] !== $b['core'][$i]) {
                return $a['core'][$i] < $b['core'][$i] ? -1 : 1;
            }
        }

        if ($a['pre'] === null || $b['pre'] === null) {
            return $a['pre'] === null && $b['pre'] === null ? 0 : ($a['pre'] !== null ? -1 : 1);
        }

        for ($i = 0; $i < min(count($a['pre']), count($b['pre'])); $i++) {
            [$x, $y] = [$a['pre'][$i], $b['pre'][$i]];

            if (is_int($x) && is_int($y)) {
                if ($x !== $y) {
                    return $x < $y ? -1 : 1;
                }
            } elseif (is_int($x) !== is_int($y)) {
                return is_int($x) ? -1 : 1;
            } elseif (($order = strcmp((string) $x, (string) $y)) !== 0) {
                return $order < 0 ? -1 : 1;
            }
        }

        return count($a['pre']) <=> count($b['pre']);
    }

    /**
     * @return array{core: list<int>, pre: list<int|string>|null}|null
     */
    private static function semver(?string $version): ?array
    {
        if ($version === null) {
            return null;
        }

        $withoutBuild = explode('+', $version)[0];
        $dash = strpos($withoutBuild, '-');
        $core = explode('.', $dash === false ? $withoutBuild : substr($withoutBuild, 0, $dash));
        $pre = $dash === false ? '' : substr($withoutBuild, $dash + 1);

        if (count($core) > 3 || ! array_all($core, fn (string $part) => preg_match('/^[0-9]+$/D', $part) === 1)) {
            return null;
        }

        $core = array_pad(array_map('intval', $core), 3, 0);

        return [
            'core' => $core,
            'pre' => $pre === '' ? null : array_map(fn (string $part) => preg_match('/^[0-9]+$/D', $part) === 1 ? (int) $part : $part, explode('.', $pre)),
        ];
    }

    /**
     * Why a pin is refused, or else the stamped release it names, as pinProblem.
     *
     * @param  array<mixed>  $pin
     * @return array{0: string|null, 1: array<string, mixed>|null}
     */
    private function pinProblem(array $pin): array
    {
        $id = $pin['id'] ?? null;
        $version = $pin['version'] ?? null;

        if (! is_string($id) || ! is_string($version)) {
            return [null, null];
        }

        $key = strtolower($id);

        if (isset($this->packs[$key]) && ! isset($this->listed[$key])) {
            return [null, null];
        }

        $listing = $this->listed[$key] ?? null;

        return match (true) {
            $listing === null => ["'{$id}' is not a listed mod, and a pack pins only listed mods", null],
            $listing['delisted'] => ["'{$id}' is delisted, and a pack pins only listed mods", null],
            $listing['type'] !== 'mod' => ["'{$id}' is listed as a {$listing['type']}, and a pack pins only mods", null],
            preg_match(self::PIN_VERSION, $version) !== 1 => [null, null],
            ! isset($listing['releases'][$version]) => ["'{$id}' has no stamped release {$version}", null],
            ! empty($listing['releases'][$version]['yanked']) => ["'{$id}' {$version} is yanked", null],
            default => [null, $listing['releases'][$version]],
        };
    }

    /**
     * @param  array<mixed>  $pin
     * @param  array<string, mixed>  $release
     * @param  array<string, Pin>  $pinned
     * @return list<string>
     */
    private function incomplete(array $pin, array $release, array $pinned): array
    {
        $problems = [];

        foreach ($release['dependencies'] ?? [] as $dependency) {
            if (! is_array($dependency)) {
                continue;
            }

            if (($dependency['kind'] ?? null) === 'required') {
                $options = $this->options($dependency);

                if (! array_any($options, fn (array $option) => $this->pinnedWithin($option, $pinned))) {
                    $problems[] = $this->named($pin).' requires '.$this->wanted($options).', and the pack '.$this->pinnedInstead($options, $pinned);
                }
            } elseif (($dependency['kind'] ?? null) === 'conflict' && is_string($dependency['id'] ?? null)
                && strtolower($dependency['id']) !== strtolower((string) $pin['id']) && $this->pinnedWithin($dependency, $pinned)) {
                $problems[] = $this->named($pin).' conflicts with '.$this->wanted([$dependency]).', and the pack pins '.$this->named($pinned[strtolower($dependency['id'])]);
            }
        }

        return $problems;
    }

    /**
     * The first pin of each id, by casefolded id.
     *
     * @param  list<mixed>  $pins
     * @return array<string, Pin>
     */
    private function pinnedMap(array $pins): array
    {
        $pinned = [];

        foreach ($pins as $pin) {
            if (is_array($pin) && is_string($pin['id'] ?? null) && is_string($pin['version'] ?? null)) {
                $pinned[strtolower($pin['id'])] ??= ['id' => $pin['id'], 'version' => $pin['version']];
            }
        }

        return $pinned;
    }

    /**
     * A bound that does not parse is no bound.
     *
     * @param  array<mixed>  $option
     * @param  array<string, Pin>  $pinned
     */
    private function pinnedWithin(array $option, array $pinned): bool
    {
        $pin = $pinned[strtolower((string) ($option['id'] ?? ''))] ?? null;

        if ($pin === null || self::compare($pin['version'], $pin['version']) === null) {
            return false;
        }

        $low = self::compare($option['min'] ?? null, $pin['version']) ?? -1;
        $high = self::compare($pin['version'], $option['max'] ?? null) ?? -1;

        return $low <= 0 && $high <= 0;
    }

    /**
     * @param  array<mixed>  $dependency
     * @return list<Option>
     */
    private function options(array $dependency): array
    {
        $options = is_array($dependency['any_of'] ?? null) ? $dependency['any_of'] : [$dependency];

        /** @var list<Option> */
        return array_values(array_filter($options, fn ($option) => is_array($option) && is_string($option['id'] ?? null)));
    }

    /**
     * @param  array<string, mixed>|null  $release
     * @return list<array<mixed>>
     */
    private function required(?array $release): array
    {
        if ($release === null || ! empty($release['yanked'])) {
            return [];
        }

        return array_values(array_filter((array) ($release['dependencies'] ?? []), fn ($dependency) => is_array($dependency) && ($dependency['kind'] ?? null) === 'required'));
    }

    /**
     * The stamped release of a listed mod that is not delisted.
     *
     * @return array<string, mixed>|null
     */
    private function stamped(string $id, string $version): ?array
    {
        $listing = $this->listed[strtolower($id)] ?? null;

        return $listing !== null && $listing['type'] === 'mod' && ! $listing['delisted'] ? ($listing['releases'][$version] ?? null) : null;
    }

    private function isMod(string $key): bool
    {
        return isset($this->listed[$key]) && $this->listed[$key]['type'] === 'mod' && ! $this->listed[$key]['delisted'];
    }

    /**
     * @param  array<mixed>  $pin
     */
    private function named(array $pin): string
    {
        return "'{$pin['id']}' {$pin['version']}";
    }

    /**
     * @param  list<array<mixed>>  $options
     */
    private function wanted(array $options): string
    {
        $described = array_map(function (array $option) {
            [$low, $high] = [$option['min'] ?? '', $option['max'] ?? ''];
            $bounds = match (true) {
                $low !== '' && $high !== '' => " {$low} to {$high}",
                $low !== '' => " {$low} or newer",
                $high !== '' => " {$high} or older",
                default => '',
            };

            return "'{$option['id']}'{$bounds}";
        }, $options);

        return count($described) === 1 ? $described[0] : 'one of '.implode(', ', $described);
    }

    /**
     * @param  list<array<mixed>>  $options
     * @param  array<string, Pin>  $pinned
     */
    private function pinnedInstead(array $options, array $pinned): string
    {
        $found = [];

        foreach ($options as $option) {
            if (isset($pinned[strtolower((string) $option['id'])])) {
                $found[] = $this->named($pinned[strtolower((string) $option['id'])]);
            }
        }

        if ($found !== []) {
            return 'pins '.implode(' and ', $found);
        }

        return count($options) === 1 ? 'does not pin it' : 'pins none of them';
    }
}
