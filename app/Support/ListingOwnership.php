<?php

namespace App\Support;

use App\Models\User;
use Devium\Toml\Toml as TomlParser;
use Illuminate\Http\Client\PendingRequest;
use Illuminate\Support\Arr;
use Illuminate\Support\Facades\Http;
use Throwable;

/**
 * Who controls a listing, decided the way the content index decides it (tools/ownership.py), so
 * that whoever may edit a listing here is also whoever's edit merges itself there.
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
            'spacedock' => self::githubRepository(
                Http::acceptJson()->timeout(15)->get('https://spacedock.info/api/mod/'.rawurlencode((string) $hosts['spacedock']))->json('source_code')
            ),
            default => null,
        };
    }

    /**
     * Whether the user owns the repository, is named by its ksa-index-<login> topic or marker file,
     * or is an admin of it, who can add that topic (which submitting a listing does).
     *
     * @param  string  $repository  As repository() resolves it.
     */
    public static function controls(User $user, string $repository, string $listingId): bool
    {
        if ($user->github_login === null) {
            return false;
        }

        $github = ListingPullRequest::github($user);
        $response = $github->get("/repos/{$repository}");

        if ($response->failed()) {
            return false;
        }

        $login = strtolower($user->github_login);

        if ((string) $response->json('owner.id') === (string) $user->github_id
            || $response->json('permissions.admin') === true
            || in_array("ksa-index-{$login}", $response->json('topics') ?? [], true)) {
            return true;
        }

        // A fork inherits its parent's files, so its marker proves nothing.
        return ! $response->json('fork') && self::markerNames($github, $repository, $listingId, $login);
    }

    private static function markerNames(PendingRequest $github, string $repository, string $listingId, string $login): bool
    {
        $response = $github->get("/repos/{$repository}/contents/".self::MARKER_PATH);

        if ($response->failed()) {
            return false;
        }

        try {
            $marker = TomlParser::decode(base64_decode($response->json('content')), asArray: true);
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
