<?php

namespace App\Support;

use App\Models\User;
use Devium\Toml\Toml as TomlParser;
use Illuminate\Http\Client\PendingRequest;
use Illuminate\Http\Client\RequestException;
use Illuminate\Support\Arr;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Sleep;

/**
 * Opens a pull request that adds or changes a listing in the content index, from the user's own
 * fork, because the index verifies ownership against the account that opens the pull request.
 */
class ListingPullRequest
{
    /**
     * The listing's authored document as it stands on the index's main branch, or null when there is none.
     *
     * Read from main rather than the snapshot, which lags behind it, so an edit cannot undo a recent change.
     *
     * @return array<string, mixed>|null
     *
     * @throws RequestException
     */
    public static function current(User $user, string $id): ?array
    {
        $response = self::github($user)->get(self::path($id), ['ref' => 'main']);

        if ($response->notFound()) {
            return null;
        }

        $document = TomlParser::decode(base64_decode($response->throw()->json('content')), asArray: true);

        // A TOML document is always a table, and a listing's keys are always strings.
        return is_array($document) ? Arr::where($document, fn ($value, $key) => is_string($key)) : null;
    }

    /**
     * @param  string|null  $releaseRepository  The GitHub repository ownership is verified against, as owner/name.
     * @return string The pull request's URL.
     *
     * @throws RequestException
     */
    public static function open(User $user, string $id, string $toml, string $title, string $body, ?string $releaseRepository, bool $update = false): string
    {
        $github = self::github($user);

        // Before the pull request opens, so its ownership check already sees the proof.
        if ($releaseRepository !== null) {
            self::proveOwnership($github, $user, $releaseRepository);
        }

        $fork = $github->post('/repos/'.ContentIndex::REPOSITORY.'/forks', ['default_branch_only' => true])
            ->throw()
            ->json('full_name');

        self::syncFork($github, $fork);

        $base = $github->get('/repos/'.ContentIndex::REPOSITORY.'/git/ref/heads/main')->throw()->json('object.sha');
        $branch = "listing/{$id}-".now()->format('YmdHis');

        $github->post("/repos/{$fork}/git/refs", ['ref' => "refs/heads/{$branch}", 'sha' => $base])->throw();

        $github->put(str_replace(ContentIndex::REPOSITORY, $fork, self::path($id)), array_filter([
            'message' => ($update ? 'Update ' : 'Add ').$id,
            'content' => base64_encode($toml),
            'branch' => $branch,
            // GitHub only replaces a file when given the blob it replaces, here the one on the branch's commit.
            'sha' => $update ? $github->get(self::path($id), ['ref' => $base])->throw()->json('sha') : null,
        ]))->throw();

        return $github->post('/repos/'.ContentIndex::REPOSITORY.'/pulls', [
            'title' => $title,
            'head' => strtok($fork, '/').":{$branch}",
            'base' => 'main',
            'body' => $body,
            'maintainer_can_modify' => true,
        ])->throw()->json('html_url');
    }

    public static function github(User $user): PendingRequest
    {
        // A Discord-only account has no token; it can still read the public index.
        return Http::when($user->github_token !== null, fn (PendingRequest $request) => $request->withToken((string) $user->github_token))
            ->baseUrl('https://api.github.com')
            ->withHeaders(['X-GitHub-Api-Version' => '2022-11-28'])
            ->acceptJson()
            ->timeout(20);
    }

    private static function path(string $id): string
    {
        return '/repos/'.ContentIndex::REPOSITORY.'/contents/listings/'.rawurlencode($id).'.toml';
    }

    /**
     * The index accepts a repository its author owns as is, and otherwise looks for the
     * ksa-index-<login> topic (tools/ownership.py in the content index). When the topic cannot be
     * added, the pull request still opens and the index's bot tells the author what to do.
     */
    private static function proveOwnership(PendingRequest $github, User $user, string $repository): void
    {
        $response = $github->get("/repos/{$repository}");

        if ($response->failed() || (string) $response->json('owner.id') === (string) $user->github_id) {
            return;
        }

        $topic = 'ksa-index-'.strtolower((string) $user->github_login);
        $topics = $response->json('topics') ?? [];

        if (! in_array($topic, $topics, true)) {
            // This call replaces every topic, so the existing ones go back in.
            $github->put("/repos/{$repository}/topics", ['names' => [...$topics, $topic]]);
        }
    }

    /**
     * A new fork takes a few seconds to become usable, so this waits for it. A fork whose own
     * main has diverged cannot sync, which is fine: the branch starts from upstream's commit.
     */
    public static function syncFork(PendingRequest $github, string $fork): void
    {
        for ($attempt = 1; $attempt <= 10; $attempt++) {
            $response = $github->post("/repos/{$fork}/merge-upstream", ['branch' => 'main']);

            if ($response->successful() || $response->status() === 409) {
                return;
            }

            Sleep::for(2)->seconds();
        }
    }
}
