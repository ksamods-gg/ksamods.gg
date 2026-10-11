<?php

namespace App\Http\Controllers;

use App\Support\ContentIndex;
use App\Support\ListingOwnership;
use App\Support\ListingPullRequest;
use App\Support\Toml;
use Closure;
use Illuminate\Http\Client\RequestException;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Arr;
use Illuminate\Validation\Rule;
use Inertia\Inertia;
use Inertia\Response;
use Symfony\Component\HttpFoundation\Response as SymfonyResponse;

/**
 * Adds and edits listings. An edit starts from the file on the index's main branch and changes
 * only the fields this form manages, so images, dependencies and the rest stay as they are.
 *
 * @phpstan-import-type Snapshot from ContentIndex
 */
class ListingController extends Controller
{
    /**
     * Where each form field lives in the authored document.
     */
    private const FIELDS = [
        'name' => 'name',
        'authors' => 'authors',
        'abstract' => 'abstract',
        'description' => 'description',
        'license' => 'license',
        'tags' => 'tags',
        'github' => 'releases.github',
        'spacedock' => 'releases.spacedock',
        'authority' => 'releases.authority',
        'forums' => 'links.forums',
        'repository' => 'links.repository',
        'bugtracker' => 'links.bugtracker',
        'game_min' => 'compatibility.game_min',
        'loader_id' => 'loader.id',
        'loader_min' => 'loader.min',
    ];

    public function create(Request $request): Response
    {
        return $this->form($request, null);
    }

    public function edit(Request $request, string $id): Response
    {
        $listing = $this->current($request, $id);
        $repository = ListingOwnership::repository($listing);

        if ($repository === null || ! ListingOwnership::controls($request->user(), $repository, $id)) {
            return Inertia::render('Listings/NotOwner', [
                'listing' => ['id' => $id, 'name' => $listing['name'] ?? $id],
                'repository' => $repository,
                'githubLogin' => $request->user()->github_login,
            ]);
        }

        return $this->form($request, $listing);
    }

    public function store(Request $request): SymfonyResponse|RedirectResponse
    {
        abort_unless($request->user()->canOpenPullRequests(), 403);

        return $this->submit($request, null);
    }

    public function update(Request $request, string $id): SymfonyResponse|RedirectResponse
    {
        abort_unless($request->user()->canOpenPullRequests(), 403);

        // Checked against the listing as it stands, not as submitted: otherwise naming a
        // repository you own in the form would hand you someone else's listing.
        $listing = $this->current($request, $id);
        $repository = ListingOwnership::repository($listing);
        abort_unless($repository !== null && ListingOwnership::controls($request->user(), $repository, $id), 403);

        return $this->submit($request, $listing);
    }

    /**
     * @param  array<string, mixed>|null  $listing
     */
    private function form(Request $request, ?array $listing): Response
    {
        $snapshot = ContentIndex::snapshot();
        $canOpenPullRequests = $request->user()->canOpenPullRequests();
        $connectFailed = $request->session()->pull('github.requested_pull_requests', false) && ! $canOpenPullRequests;

        if ($connectFailed) {
            // A GitHub App (client id Iv...) ignores OAuth scopes; this flow needs an OAuth App.
            logger()->warning('GitHub granted no public_repo scope', ['user' => $request->user()->id, 'scopes' => $request->user()->github_scopes]);
        }

        $values = $listing === null ? null : ['id' => $listing['id'], 'type' => $listing['type']]
            + array_map(fn (string $path) => Arr::get($listing, $path), self::FIELDS);

        if ($values !== null) {
            $values['authors'] = implode(', ', $values['authors'] ?? []);
            $values['description'] = rtrim($values['description'] ?? '');
            $values['tags'] ??= [];
        }

        return Inertia::render('Listings/Form', [
            'listing' => $values,
            'canOpenPullRequests' => $canOpenPullRequests,
            'connectFailed' => $connectFailed,
            'githubLogin' => $request->user()->github_login,
            // A listing keeps a tag, game version or loader it already names, even one outside these lists.
            'tags' => [
                ...array_map(fn (array $tag) => ['tag' => $tag['tag'], 'name' => $tag['name']], $snapshot['tags']['mod'] ?? []),
                ...array_map(
                    fn (string $tag) => ['tag' => $tag, 'name' => $tag],
                    array_values(array_diff(Arr::wrap($values['tags'] ?? []), array_column($snapshot['tags']['mod'] ?? [], 'tag'))),
                ),
            ],
            'gameVersions' => array_values(array_unique([
                ...array_reverse($snapshot['game_versions']['versions'] ?? []),
                ...array_filter([$values['game_min'] ?? null]),
            ])),
            'loaders' => array_values(array_unique([...$this->loaderIds($snapshot), ...array_filter([$values['loader_id'] ?? null])])),
        ]);
    }

    /**
     * @param  array<string, mixed>|null  $listing  The listing being edited, or null for a new one.
     */
    private function submit(Request $request, ?array $listing): SymfonyResponse|RedirectResponse
    {
        $snapshot = ContentIndex::snapshot();
        $listedIds = collect($snapshot['listings'])->pluck('id');
        $isMod = ($listing['type'] ?? 'mod') === 'mod';

        // Patterns follow schemas/authored.schema.json in the content index.
        $validated = $request->validate([
            'id' => $listing !== null ? ['exclude'] : [
                'required', 'string',
                'regex:/^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$/D',
                'not_regex:/^(core|con|prn|aux|nul|com[1-9]|lpt[1-9])(\.|$)/i',
                function (string $attribute, string $value, Closure $fail) use ($listedIds) {
                    if ($listedIds->contains(fn (string $id) => strcasecmp($id, $value) === 0)) {
                        $fail('A listing with this id already exists.');
                    }
                },
            ],
            'name' => ['required', 'string', 'max:100'],
            'authors' => ['required', 'string', 'max:255'],
            'abstract' => ['required', 'string', 'max:300'],
            'description' => ['nullable', 'string', 'max:60000'],
            'license' => ['required', 'string', 'regex:/^[A-Za-z0-9.:+()-]+(?: (?:AND|OR|WITH) [A-Za-z0-9.:+()-]+)*$/D'],
            'tags' => ['nullable', 'array'],
            'tags.*' => [Rule::in([...collect($snapshot['tags']['mod'] ?? [])->pluck('tag'), ...($listing['tags'] ?? [])])],
            'forums' => ['required', 'string', 'regex:~^https://forums\.ahwoo\.com/(?:index\.php\?)?(?:threads/|forums/(?:[^\s/?#\x00-\x1f]+/)+)(?:[^\s/?#\x00-\x1f]*\.)?[0-9]+(?:[/?#][^\s\x00-\x1f]*)?$~D'],
            'repository' => ['nullable', 'string', 'regex:~^https://[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?(?::[0-9]{1,5})?(?:[/?#][^\s\x00-\x1f]*)?$~D'],
            'bugtracker' => ['nullable', 'string', 'regex:~^https://[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?(?::[0-9]{1,5})?(?:[/?#][^\s\x00-\x1f]*)?$~D'],
            'github' => ['nullable', 'required_without:spacedock', 'string', 'regex:~^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?/[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$~D'],
            'spacedock' => ['nullable', 'required_without:github', 'integer', 'min:1'],
            'authority' => [Rule::requiredIf($request->filled(['github', 'spacedock'])), 'nullable', Rule::in(['github', 'spacedock'])],
            'game_min' => ['required', Rule::in([...($snapshot['game_versions']['versions'] ?? []), ...array_filter([$listing['compatibility']['game_min'] ?? null])])],
            // A mod-loader has no [loader] section of its own.
            'loader_id' => $isMod ? ['nullable', Rule::in([...$this->loaderIds($snapshot), ...array_filter([$listing['loader']['id'] ?? null])])] : ['exclude'],
            'loader_min' => $isMod ? ['nullable', 'required_with:loader_id', 'regex:/^(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){0,2}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/D'] : ['exclude'],
        ], [
            'github.required_without' => 'Give a GitHub repository or a SpaceDock mod id, so the index can find your releases.',
            'spacedock.required_without' => 'Give a GitHub repository or a SpaceDock mod id, so the index can find your releases.',
            'forums.regex' => 'Use a link to your thread on forums.ahwoo.com, such as https://forums.ahwoo.com/threads/my-mod.783/.',
            'license.regex' => 'Use an SPDX license expression, such as MIT or GPL-2.0-only AND CC-BY-SA-4.0.',
        ]);

        $description = trim(str_replace("\r\n", "\n", $validated['description'] ?? ''));
        $values = [
            ...$validated,
            'authors' => array_values(array_filter(array_map('trim', explode(',', $validated['authors'])))),
            'description' => $description === '' ? null : "{$description}\n",
            'spacedock' => isset($validated['spacedock']) ? (int) $validated['spacedock'] : null,
        ];

        $document = $listing ?? ['spec_version' => 1, 'id' => $validated['id'], 'type' => 'mod'];

        foreach (self::FIELDS as $field => $path) {
            if (! $isMod && str_starts_with($path, 'loader.')) {
                continue;
            }

            $value = $values[$field] ?? null;
            $value === null || $value === [] ? Arr::forget($document, $path) : Arr::set($document, $path, $value);
        }

        // Without a loader, its bounds (such as max) go too.
        if ($isMod && ! isset($values['loader_id'])) {
            unset($document['loader']);
        }

        $id = $document['id'];
        $verb = $listing === null ? 'Add' : 'Update';

        try {
            $url = ListingPullRequest::open(
                $request->user(),
                $id,
                Toml::encode($document),
                "{$verb} {$validated['name']}",
                ($listing === null ? 'Adds' : 'Updates')." `listings/{$id}.toml`.\n\nSubmitted with ".config('app.url').'.',
                ListingOwnership::repository($document),
                update: $listing !== null,
            );
        } catch (RequestException $e) {
            report($e);

            return back()->withErrors([
                'pull_request' => 'GitHub refused the pull request: '.($e->response->json('message') ?? 'unknown error').'. If this keeps happening, reconnect GitHub and try again.',
            ]);
        }

        return Inertia::location($url);
    }

    /**
     * @return array<string, mixed>
     */
    private function current(Request $request, string $id): array
    {
        return ListingPullRequest::current($request->user(), $id) ?? abort(404);
    }

    /**
     * @param  Snapshot  $snapshot
     * @return list<string>
     */
    private function loaderIds(array $snapshot): array
    {
        return array_values(array_map(
            fn (array $listing) => $listing['id'],
            array_filter($snapshot['listings'], fn (array $listing) => $listing['authored']['type'] === 'mod-loader'),
        ));
    }
}
