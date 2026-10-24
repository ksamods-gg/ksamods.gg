<?php

namespace App\Support\Share;

use App\Models\Share;
use App\Support\ListingPullRequest;
use Illuminate\Http\Client\PendingRequest;
use Illuminate\Http\Client\RequestException;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\Http;

/**
 * The host's bot account on GitHub (rule 4): it opens listing pull requests on the authored
 * repository from its own fork, like any author, and never merges, approves or labels (rule 14).
 * Authenticated with a classic token of the bot user, SHARE_BOT_TOKEN.
 */
class ShareBot
{
    /**
     * Opens a pull request with the listing's document, or pushes it to the listing's open one.
     * The caller has already written the attestation of exactly these files (rules 9 and 13).
     *
     * @param  array<string, string|null>  $files  Paths with their new content, or null to delete.
     * @return string The pull request's URL.
     *
     * @throws RequestException
     */
    public static function propose(Share $share, array $files, string $title, string $body): string
    {
        $github = self::github();
        $repository = config('share.authored_repository');
        $fork = $github->post("/repos/{$repository}/forks", ['default_branch_only' => true])->throw()->json('full_name');
        $owner = strtok($fork, '/');

        // Push to the open pull request of this listing when there is one, so a listing has one at a time.
        $open = $share->branch !== null && self::isOpen($github, $repository, "{$owner}:{$share->branch}");

        if ($open) {
            $branch = (string) $share->branch;
            $parent = $github->get("/repos/{$fork}/git/ref/heads/{$branch}")->throw()->json('object.sha');
        } else {
            ListingPullRequest::syncFork($github, $fork);
            $branch = "share/{$share->listing_id}-".now()->format('YmdHis');
            $parent = $github->get("/repos/{$repository}/git/ref/heads/main")->throw()->json('object.sha');
        }

        $baseTree = $github->get("/repos/{$fork}/git/commits/{$parent}")->throw()->json('tree.sha');
        $tree = $github->post("/repos/{$fork}/git/trees", [
            'base_tree' => $baseTree,
            'tree' => array_map(fn (string $path, ?string $content) => $content === null
                ? ['path' => $path, 'mode' => '100644', 'type' => 'blob', 'sha' => null]
                : ['path' => $path, 'mode' => '100644', 'type' => 'blob', 'content' => $content], array_keys($files), $files),
        ])->throw()->json('sha');
        $commit = $github->post("/repos/{$fork}/git/commits", [
            'message' => $title,
            'tree' => $tree,
            'parents' => [$parent],
        ])->throw()->json('sha');

        if ($open) {
            $github->patch("/repos/{$fork}/git/refs/heads/{$branch}", ['sha' => $commit])->throw();

            return (string) $share->pull_request_url;
        }

        $github->post("/repos/{$fork}/git/refs", ['ref' => "refs/heads/{$branch}", 'sha' => $commit])->throw();
        $url = $github->post("/repos/{$repository}/pulls", [
            'title' => $title,
            'head' => "{$owner}:{$branch}",
            'base' => 'main',
            'body' => $body,
            'maintainer_can_modify' => true,
        ])->throw()->json('html_url');

        $share->forceFill(['branch' => $branch, 'pull_request_url' => $url])->save();

        return $url;
    }

    /**
     * Ids that open pull requests on the authored repository add, as listings of any type. Cached
     * briefly, since every new id is checked against them (RFC 0094, Owners).
     *
     * @return list<string>
     */
    public static function idsInOpenPullRequests(): array
    {
        return Cache::remember('share:open-pull-request-ids', 300, function () {
            $github = self::github();
            $repository = config('share.authored_repository');
            $ids = [];

            foreach ($github->get("/repos/{$repository}/pulls", ['state' => 'open', 'per_page' => 100])->throw()->json() as $pull) {
                foreach ($github->get("/repos/{$repository}/pulls/{$pull['number']}/files", ['per_page' => 100])->json() ?? [] as $file) {
                    if (preg_match('#^(?:listings|vehicles|saves|packs)/([^/]+)\.toml$#', $file['filename'], $match) === 1) {
                        $ids[] = $match[1];
                    }
                }
            }

            return array_values(array_unique($ids));
        });
    }

    private static function isOpen(PendingRequest $github, string $repository, string $head): bool
    {
        return $github->get("/repos/{$repository}/pulls", ['state' => 'open', 'head' => $head])->throw()->json() !== [];
    }

    public static function github(): PendingRequest
    {
        return Http::withToken((string) config('share.bot_token'))
            ->baseUrl('https://api.github.com')
            ->withHeaders(['X-GitHub-Api-Version' => '2022-11-28'])
            ->acceptJson()
            ->timeout(20);
    }
}
