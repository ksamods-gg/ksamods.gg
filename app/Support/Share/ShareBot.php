<?php

namespace App\Support\Share;

use App\Models\Share;
use App\Support\ListingPullRequest;
use Illuminate\Http\Client\PendingRequest;
use Illuminate\Http\Client\RequestException;
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\Http;
use RuntimeException;

/**
 * The host's bot on GitHub (rule 4): a GitHub App, whose bot user (such as ksamods-gg[bot]) opens
 * listing pull requests on the authored repository from the host's fork, like any author, and
 * never merges, approves or labels (rule 14). The App has no write access to code, since an App
 * has the same permissions on every installation, the authored repository's included. Branches
 * are pushed to the fork with a token for the fork alone.
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
        $forkApi = self::fork();
        $repository = config('share.authored_repository');
        $fork = (string) config('share.fork');
        $owner = strtok($fork, '/');

        // Push to the open pull request of this listing when there is one, so a listing has one at a time.
        $open = $share->branch !== null && self::isOpen($github, $repository, "{$owner}:{$share->branch}");

        if ($open) {
            $branch = (string) $share->branch;
            $parent = $forkApi->get("/repos/{$fork}/git/ref/heads/{$branch}")->throw()->json('object.sha');
        } else {
            ListingPullRequest::syncFork($forkApi, $fork);
            $branch = "share/{$share->listing_id}-".now()->format('YmdHis');
            // The authored repository is read with the App alone: the fork's token is for the fork.
            $parent = $github->get("/repos/{$repository}/git/ref/heads/main")->throw()->json('object.sha');
        }

        $baseTree = $forkApi->get("/repos/{$fork}/git/commits/{$parent}")->throw()->json('tree.sha');
        $tree = $forkApi->post("/repos/{$fork}/git/trees", [
            'base_tree' => $baseTree,
            'tree' => array_map(fn (string $path, ?string $content) => $content === null
                ? ['path' => $path, 'mode' => '100644', 'type' => 'blob', 'sha' => null]
                : ['path' => $path, 'mode' => '100644', 'type' => 'blob', 'content' => $content], array_keys($files), $files),
        ])->throw()->json('sha');
        // Committed as the bot, not as the person the fork's token belongs to.
        $bot = ['name' => self::botLogin(), 'email' => config('share.bot_id').'+'.self::botLogin().'@users.noreply.github.com'];
        $commit = $forkApi->post("/repos/{$fork}/git/commits", [
            'message' => $title,
            'tree' => $tree,
            'parents' => [$parent],
            'author' => $bot,
            'committer' => $bot,
        ])->throw()->json('sha');

        if ($open) {
            $forkApi->patch("/repos/{$fork}/git/refs/heads/{$branch}", ['sha' => $commit])->throw();

            return (string) $share->pull_request_url;
        }

        $forkApi->post("/repos/{$fork}/git/refs", ['ref' => "refs/heads/{$branch}", 'sha' => $commit])->throw();
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

    /**
     * Whether the listing's pull request was merged, so the index has or will have it.
     *
     * @throws RequestException
     */
    public static function merged(Share $share): bool
    {
        return $share->pull_request_url !== null && (bool) self::github()
            ->get('/repos/'.config('share.authored_repository').'/pulls/'.basename($share->pull_request_url))->throw()->json('merged');
    }

    /**
     * Closes the listing's pull request and deletes its branch from the fork.
     *
     * @throws RequestException
     */
    public static function withdraw(Share $share): void
    {
        if ($share->pull_request_url !== null) {
            self::github()->patch('/repos/'.config('share.authored_repository').'/pulls/'.basename($share->pull_request_url), ['state' => 'closed'])->throw();
        }

        if ($share->branch !== null) {
            $response = self::fork()->delete('/repos/'.config('share.fork')."/git/refs/heads/{$share->branch}");

            if ($response->status() !== 422) {
                $response->throw();
            }
        }
    }

    private static function isOpen(PendingRequest $github, string $repository, string $head): bool
    {
        return $github->get("/repos/{$repository}/pulls", ['state' => 'open', 'head' => $head])->throw()->json() !== [];
    }

    /**
     * GitHub's API as the App's installation on the authored repository, for its pull requests.
     */
    public static function github(): PendingRequest
    {
        return self::api()->withToken(self::installationToken());
    }

    /**
     * GitHub's API with the fork's token: a fine-grained token for the fork alone, with Contents
     * read and write.
     */
    private static function fork(): PendingRequest
    {
        $token = config('share.fork_token');

        if (blank($token)) {
            throw new RuntimeException('SHARE_FORK_TOKEN must be a token with Contents read and write on '.config('share.fork').'.');
        }

        return self::api()->withToken((string) $token);
    }

    /**
     * An installation token lasts an hour; one is kept for 50 minutes.
     *
     * @throws RequestException
     */
    private static function installationToken(): string
    {
        return Cache::remember('share:github-installation-token', 3000, function () {
            $app = self::api()->withToken(self::appToken());
            $installation = $app->get('/repos/'.config('share.authored_repository').'/installation')->throw()->json('id');

            return (string) $app->post("/app/installations/{$installation}/access_tokens")->throw()->json('token');
        });
    }

    /**
     * The App's bot user, such as ksamods-gg[bot].
     *
     * @throws RequestException
     */
    private static function botLogin(): string
    {
        return Cache::rememberForever('share:github-app-bot', fn () => self::api()->withToken(self::appToken())->get('/app')->throw()->json('slug').'[bot]');
    }

    /**
     * The App's own token: a JWT signed with its private key, valid for ten minutes, issued a
     * minute in the past against clock drift (GitHub's "Generating a JWT for a GitHub App").
     */
    private static function appToken(): string
    {
        // Env values often carry the key's line breaks as a literal \n.
        $key = openssl_pkey_get_private(str_replace('\n', "\n", (string) config('share.github_app.private_key')));

        if ($key === false || blank(config('share.github_app.id'))) {
            throw new RuntimeException('SHARE_GITHUB_APP_ID and SHARE_GITHUB_APP_PRIVATE_KEY must be the GitHub App\'s id and private key.');
        }

        $encode = fn (string $data) => rtrim(strtr(base64_encode($data), '+/', '-_'), '=');
        $unsigned = $encode((string) json_encode(['alg' => 'RS256', 'typ' => 'JWT']))
            .'.'.$encode((string) json_encode(['iat' => now()->subMinute()->timestamp, 'exp' => now()->addMinutes(9)->timestamp, 'iss' => (string) config('share.github_app.id')]));
        openssl_sign($unsigned, $signature, $key, OPENSSL_ALGO_SHA256);

        return $unsigned.'.'.$encode($signature);
    }

    private static function api(): PendingRequest
    {
        return Http::baseUrl('https://api.github.com')
            ->withHeaders(['X-GitHub-Api-Version' => '2022-11-28'])
            ->acceptJson()
            ->timeout(20);
    }
}
