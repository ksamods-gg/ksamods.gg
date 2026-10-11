<?php

namespace App\Support;

use App\Models\User;
use Devium\Toml\Toml as TomlParser;
use Illuminate\Http\Client\Pool;
use Illuminate\Http\Client\Response;
use Illuminate\Support\Arr;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\Http;
use Throwable;

/**
 * Who controls a listing, decided the way the content index decides it (tools/ownership.py), so
 * that whoever may edit a listing here is also whoever's edit merges itself there.
 *
 * A user's role is 'owner' when their account owns the repository, and 'admin', 'topic' or
 * 'marker' when they control it some other way.
 */
class ListingOwnership
{
    public const MARKER_PATH = '.github/ksa-content-index.toml';

    /**
     * The GitHub repository ownership binds to: the release host the listing names, a SpaceDock mod's
     * source code link, or else the repository link. Null when nothing names a GitHub repository.
     *
     * @param  array<string, mixed>  $listing
     */
    public static function repository(array $listing): ?string
    {
        $releases = Arr::wrap($listing['releases'] ?? []);
        $hosts = Arr::only($releases, ['github', 'spacedock']);

        if ($hosts === []) {
            return self::githubRepository(Arr::get($listing, 'links.repository'));
        }

        $authority = count($hosts) === 1 ? array_key_first($hosts) : ($releases['authority'] ?? null);

        return match ($authority) {
            'github' => is_string($hosts['github']) ? $hosts['github'] : null,
            // Only a SpaceDock mod's own authors can set its source code link.
            'spacedock' => self::githubRepository(Cache::remember(
                'spacedock:source:'.$hosts['spacedock'],
                3600,
                fn () => Http::acceptJson()->timeout(15)->get('https://spacedock.info/api/mod/'.rawurlencode((string) $hosts['spacedock']))->json('source_code'),
            )),
            default => null,
        };
    }

    /**
     * Whether the user controls the repository, as repository() resolves it.
     */
    public static function controls(User $user, string $repository, string $listingId): bool
    {
        return self::roles($user, [$listingId => $repository]) !== [];
    }

    /**
     * The user's role for each listing they control, looked up concurrently.
     *
     * @param  array<string, string>  $repositories  Repository by listing id.
     * @return array<string, string> Role by listing id, for the listings the user controls.
     */
    public static function roles(User $user, array $repositories): array
    {
        if ($user->github_login === null || $repositories === []) {
            return [];
        }

        $login = strtolower($user->github_login);
        $answers = self::fetch($user, array_map(fn (string $repository) => "/repos/{$repository}", $repositories));
        $roles = [];
        $markers = [];

        foreach ($repositories as $id => $repository) {
            $answer = $answers[$id];

            if ($answer === null) {
                continue;
            }

            $roles[$id] = match (true) {
                (string) $answer->json('owner.id') === (string) $user->github_id => 'owner',
                // An admin can add the topic, which submitting a listing does.
                $answer->json('permissions.admin') === true => 'admin',
                in_array("ksa-index-{$login}", $answer->json('topics') ?? [], true) => 'topic',
                default => null,
            };

            // A fork inherits its parent's files, so its marker proves nothing.
            if ($roles[$id] === null && ! $answer->json('fork')) {
                $markers[$id] = "/repos/{$repository}/contents/".self::MARKER_PATH;
            }
        }

        foreach (self::fetch($user, $markers) as $id => $answer) {
            if ($answer !== null && self::markerNames($answer, (string) $id, $login)) {
                $roles[$id] = 'marker';
            }
        }

        return array_filter($roles);
    }

    /**
     * GitHub's answers to the paths, all at once, keyed like the paths; null where it answered no.
     *
     * @param  array<string, string>  $paths
     * @return array<string, Response|null>
     */
    private static function fetch(User $user, array $paths): array
    {
        if ($paths === []) {
            return [];
        }

        $answers = Http::pool(fn (Pool $pool) => array_map(
            fn (string $id) => $pool->as($id)
                ->withToken((string) $user->github_token)
                ->baseUrl('https://api.github.com')
                ->withHeaders(['X-GitHub-Api-Version' => '2022-11-28'])
                ->acceptJson()
                ->timeout(20)
                ->get($paths[$id]),
            array_map('strval', array_keys($paths)),
        ));

        return array_map(
            fn ($answer) => $answer instanceof Response && $answer->successful() ? $answer : null,
            array_intersect_key($answers, $paths),
        );
    }

    private static function markerNames(Response $answer, string $listingId, string $login): bool
    {
        try {
            $marker = TomlParser::decode(base64_decode((string) $answer->json('content')), asArray: true);
        } catch (Throwable) {
            return false;
        }

        if (! is_array($marker)) {
            return false;
        }

        $claimed = $marker['login'] ?? $marker['account'] ?? null;
        $for = $marker['id'] ?? $marker['listing'] ?? null;

        // Naming only a login covers every listing on the repository.
        return is_string($claimed)
            && strtolower($claimed) === $login
            && (! is_string($for) || strcasecmp($for, $listingId) === 0);
    }

    private static function githubRepository(mixed $url): ?string
    {
        if (! is_string($url) || ! preg_match('~^https?://(?:www\.)?github\.com/([A-Za-z0-9][A-Za-z0-9._-]*)/([A-Za-z0-9][A-Za-z0-9._-]*?)(?:\.git)?(?:[/?#]|$)~i', $url, $match)) {
            return null;
        }

        return "{$match[1]}/{$match[2]}";
    }
}
