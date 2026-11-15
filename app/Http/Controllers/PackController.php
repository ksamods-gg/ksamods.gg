<?php

namespace App\Http\Controllers;

use App\Support\ContentIndex;
use App\Support\ListingPullRequest;
use App\Support\PackRules;
use App\Support\Share\Screenshots;
use App\Support\Share\ShareError;
use App\Support\Toml;
use Closure;
use Illuminate\Http\Client\RequestException;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Carbon;
use Illuminate\Support\Collection;
use Illuminate\Validation\Rule;
use Illuminate\Validation\ValidationException;
use Inertia\Inertia;
use Inertia\Response;
use Symfony\Component\HttpFoundation\Response as SymfonyResponse;

/**
 * Mod packs from the content index: a named set of mods at pinned versions, which Borea installs
 * together with their dependencies. Each pack keeps its versions, newest first.
 *
 * @phpstan-import-type Pack from ContentIndex
 */
class PackController extends Controller
{
    public function index(): Response
    {
        return Inertia::render('Packs/Index', [
            'packs' => collect(ContentIndex::snapshot()['packs'])
                ->map(fn (array $pack) => $this->summary($pack))
                ->sortByDesc('updated_at')
                ->values(),
        ]);
    }

    public function show(string $id): Response
    {
        $snapshot = ContentIndex::snapshot();
        $pack = collect($snapshot['packs'])->firstWhere('id', $id) ?? abort(404);
        $authored = $pack['versions'][0]['authored'];
        $listings = collect($snapshot['listings'])->keyBy('id');

        return Inertia::render('Packs/Show', [
            'pack' => [
                ...$this->summary($pack),
                'description' => app(ModController::class)->markdown($authored['description'] ?? '', new Collection),
                'license' => $authored['license'] ?? null,
                'links' => $authored['links'] ?? [],
                // The mods at the versions the pack pins, with what the index says of each.
                'mods' => collect($authored['mods'] ?? [])->map(fn (array $mod) => [
                    'id' => $mod['id'],
                    'version' => $mod['version'],
                    'name' => $listings[$mod['id']]['authored']['name'] ?? $mod['id'],
                    'abstract' => $listings[$mod['id']]['authored']['abstract'] ?? null,
                    'icon' => $listings[$mod['id']]['authored']['images']['icon']['url'] ?? null,
                    'listed' => $listings->has($mod['id']),
                ])->values(),
                'versions' => collect($pack['versions'])->map(fn (array $version) => [
                    'version' => $version['authored']['version'],
                    'date' => $version['authored']['released_at'] ?? null,
                    'mods' => count($version['authored']['mods'] ?? []),
                ])->values(),
            ],
        ]);
    }

    /**
     * The form for a new pack.
     */
    public function create(Request $request): Response
    {
        return $this->form($request, null);
    }

    /**
     * The form for the next version of a pack, filled from its newest one. Only the account its
     * owner record names gets it: the index merges later versions only from that account.
     */
    public function newVersion(Request $request, string $id): Response|RedirectResponse
    {
        $pack = collect(ContentIndex::snapshot()['packs'])->firstWhere('id', $id) ?? abort(404);
        $owner = ListingPullRequest::github($request->user())->get('/repos/'.ContentIndex::REPOSITORY."/contents/packs/{$pack['id']}/owner.json", ['ref' => 'main']);
        $ownerId = $owner->successful() ? json_decode((string) base64_decode((string) $owner->json('content')), true)['github_id'] ?? null : null;

        if ($ownerId === null || (string) $ownerId !== (string) $request->user()->github_id) {
            Inertia::flash('status', __('Only the GitHub account that owns this pack can publish its next version.'));

            return to_route('packs.show', $pack['id']);
        }

        return $this->form($request, $pack);
    }

    /**
     * What the pinned mods need, for the form as it changes: errors and notes in the index's
     * words, the dependencies to add, and the oldest game build the pinned releases allow.
     */
    public function check(Request $request): JsonResponse
    {
        $mods = $this->pins($request->query('mods'));
        $rules = PackRules::fromIndex();
        $missing = $rules->missing($mods);

        return response()->json([
            ...$rules->check([...$mods, ...$missing['pins']]),
            'adds' => $missing['pins'],
            'unresolved' => $missing['notes'],
            'game_min' => $rules->gameMin([...$mods, ...$missing['pins']]),
        ]);
    }

    public function store(Request $request): SymfonyResponse|RedirectResponse
    {
        abort_unless($request->user()->canOpenPullRequests(), 403);

        $snapshot = ContentIndex::snapshot();
        $pack = $request->filled('of') ? (collect($snapshot['packs'])->firstWhere('id', $request->input('of')) ?? abort(404)) : null;
        $takenIds = [...array_column($snapshot['listings'], 'id'), ...array_column($snapshot['packs'], 'id')];
        $highest = $pack['versions'][0]['authored']['version'] ?? null;

        // Patterns follow schemas/authored.schema.json in the content index.
        $validated = $request->validate([
            'id' => $pack !== null ? ['exclude'] : [
                'required', 'string',
                'regex:/^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$/D',
                'not_regex:/^(core|con|prn|aux|nul|com[1-9]|lpt[1-9])(\.|$)/i',
                function (string $attribute, string $value, Closure $fail) use ($takenIds) {
                    if (array_any($takenIds, fn (string $id) => strcasecmp($id, $value) === 0)) {
                        $fail(__('A mod or pack with this id already exists.'));
                    }
                },
            ],
            'version' => ['required', 'string', 'regex:/^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$/D',
                function (string $attribute, string $value, Closure $fail) use ($highest) {
                    if ($highest !== null && (PackRules::compare($value, $highest) ?? 1) <= 0) {
                        $fail(__('The version must be higher than :version, the newest version of this pack.', ['version' => $highest]));
                    }
                },
            ],
            'name' => ['required', 'string', 'max:100'],
            'authors' => ['required', 'string', 'max:255'],
            'abstract' => ['required', 'string', 'max:300'],
            'description' => ['nullable', 'string', 'max:60000'],
            'changelog' => $pack !== null ? ['nullable', 'string', 'max:10000'] : ['exclude'],
            'license' => ['required', 'string', 'regex:/^[A-Za-z0-9.:+()-]+(?: (?:AND|OR|WITH) [A-Za-z0-9.:+()-]+)*$/D'],
            'tags' => ['nullable', 'array'],
            'tags.*' => [Rule::in([...collect($snapshot['tags']['mod'] ?? [])->pluck('tag'), ...($pack['versions'][0]['authored']['tags'] ?? [])])],
            'forums' => ['required', 'string', 'regex:~^https://forums\.ahwoo\.com/(?:index\.php\?)?(?:threads/|forums/(?:[^\s/?#\x00-\x1f]+/)+)(?:[^\s/?#\x00-\x1f]*\.)?[0-9]+(?:[/?#][^\s\x00-\x1f]*)?$~D'],
            'repository' => ['nullable', 'string', 'regex:~^https://[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?(?::[0-9]{1,5})?(?:[/?#][^\s\x00-\x1f]*)?$~D'],
            'game_min' => ['nullable', Rule::in($snapshot['game_versions']['versions'] ?? [])],
            'icon' => ['nullable', 'file', 'max:10240'],
            'mods' => ['required', 'array', 'min:1', 'max:100'],
            'mods.*.id' => ['required', 'string'],
            'mods.*.version' => ['required', 'string'],
        ], [
            'forums.regex' => __('Use a link to your pack\'s thread on forums.ahwoo.com, such as https://forums.ahwoo.com/threads/my-pack.783/.'),
            'license.regex' => __('Use an SPDX license expression, such as MIT or MIT AND CC-BY-4.0.'),
            'version.regex' => __('Use a version such as 1.0.0.'),
        ]);

        $mods = $this->pins($validated['mods']);
        $problems = PackRules::fromIndex()->check($mods)['errors'];

        if ($problems !== []) {
            throw ValidationException::withMessages(['mods' => $problems]);
        }

        $id = $pack['id'] ?? $validated['id'];

        try {
            $icon = $request->hasFile('icon')
                ? Screenshots::icon((string) file_get_contents($request->file('icon')->getRealPath()), "images/pack/{$id}")
                : ($pack['versions'][0]['authored']['images']['icon'] ?? null);
        } catch (ShareError $error) {
            throw ValidationException::withMessages(['icon' => $error->getMessage()]);
        }

        $description = trim(str_replace("\r\n", "\n", $validated['description'] ?? ''));
        $changelog = trim(str_replace("\r\n", "\n", $validated['changelog'] ?? ''));
        $document = array_filter([
            'spec_version' => 1,
            'id' => $id,
            'type' => 'modpack',
            'name' => $validated['name'],
            'authors' => array_values(array_filter(array_map('trim', explode(',', $validated['authors'])))),
            'version' => $validated['version'],
            'released_at' => now()->utc()->format('Y-m-d\TH:i:s\Z'),
            'abstract' => $validated['abstract'],
            'description' => $description === '' ? null : "{$description}\n",
            'changelog' => $changelog === '' ? null : "{$changelog}\n",
            'license' => $validated['license'],
            'tags' => array_values($validated['tags'] ?? []),
            'links' => array_filter(['forums' => $validated['forums'], 'repository' => $validated['repository'] ?? null]),
            'compatibility' => array_filter(['game_min' => $validated['game_min'] ?? null]),
            'images' => $icon !== null ? ['icon' => $icon] : null,
            'mods' => $mods,
        ], fn ($value) => $value !== null && $value !== []);

        $files = ["packs/{$id}/{$validated['version']}.toml" => Toml::encode($document)];

        // The first version claims the id for the account that opens the pull request.
        if ($pack === null) {
            $files["packs/{$id}/owner.json"] = json_encode(['github_login' => $request->user()->github_login, 'github_id' => (int) $request->user()->github_id], JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES)."\n";
        }

        try {
            $url = ListingPullRequest::openFiles(
                $request->user(),
                "pack/{$id}",
                $files,
                "Add {$id} {$validated['version']}",
                $pack === null ? "Add the {$validated['name']} pack" : "{$validated['name']} {$validated['version']}",
                ($pack === null ? "Adds `packs/{$id}/{$validated['version']}.toml` and its owner record." : "Adds `packs/{$id}/{$validated['version']}.toml`.")
                    ."\n\nSubmitted with ".config('app.url').'.',
            );
        } catch (RequestException $e) {
            report($e);

            return back()->withErrors([
                'pull_request' => __('GitHub refused the pull request: :message. If this keeps happening, reconnect GitHub and try again.', ['message' => $e->response->json('message') ?? __('unknown error')]),
            ]);
        }

        return Inertia::location($url);
    }

    /**
     * The form, empty or filled from a pack's newest version with the next patch version.
     *
     * @param  Pack|null  $pack
     */
    private function form(Request $request, ?array $pack): Response
    {
        $snapshot = ContentIndex::snapshot();
        $authored = $pack['versions'][0]['authored'] ?? null;

        return Inertia::render('Packs/Form', [
            'pack' => $authored === null ? null : [
                'id' => $pack['id'],
                'version' => preg_replace_callback('/^(\d+)\.(\d+)\.(\d+).*$/', fn (array $m) => "{$m[1]}.{$m[2]}.".((int) $m[3] + 1), $authored['version']),
                'previous' => $authored['version'],
                'name' => $authored['name'],
                'authors' => implode(', ', $authored['authors'] ?? []),
                'abstract' => $authored['abstract'] ?? '',
                'description' => rtrim($authored['description'] ?? ''),
                'license' => $authored['license'] ?? '',
                'tags' => $authored['tags'] ?? [],
                'forums' => $authored['links']['forums'] ?? '',
                'repository' => $authored['links']['repository'] ?? '',
                'game_min' => $authored['compatibility']['game_min'] ?? null,
                'icon' => $authored['images']['icon']['url'] ?? null,
                'mods' => $authored['mods'] ?? [],
            ],
            'canOpenPullRequests' => $request->user()->canOpenPullRequests(),
            // Every listed mod with its releases, newest first; a yanked release cannot be pinned.
            'mods' => collect($snapshot['listings'])
                ->filter(fn (array $listing) => $listing['authored']['type'] === 'mod' && ($listing['releases'] ?? []) !== [])
                ->map(fn (array $listing) => [
                    'id' => $listing['id'],
                    'name' => $listing['authored']['name'],
                    'icon' => $listing['authored']['images']['icon']['url'] ?? null,
                    'releases' => collect($listing['releases'])
                        ->reject(fn (array $release) => ! empty($release['yanked']))
                        ->sort(fn (array $a, array $b) => PackRules::compare($b['version'], $a['version']) ?? 0)
                        ->map(fn (array $release) => ['version' => $release['version'], 'status' => $release['release_status']])
                        ->values(),
                ])
                ->sortBy('name', SORT_NATURAL | SORT_FLAG_CASE)
                ->values(),
            'tags' => array_map(fn (array $tag) => ['tag' => $tag['tag'], 'name' => $tag['name']], $snapshot['tags']['mod'] ?? []),
            'gameVersions' => array_reverse($snapshot['game_versions']['versions'] ?? []),
        ]);
    }

    /**
     * The id and version of each pin, as strings.
     *
     * @return list<array{id: string, version: string}>
     */
    private function pins(mixed $mods): array
    {
        return array_values(array_map(
            fn (array $pin) => ['id' => (string) $pin['id'], 'version' => (string) $pin['version']],
            array_filter(is_array($mods) ? $mods : [], fn ($pin) => is_array($pin) && is_scalar($pin['id'] ?? null) && is_scalar($pin['version'] ?? null)),
        ));
    }

    /**
     * What a row and the top of the page show, from the pack's newest version.
     *
     * @param  Pack  $pack
     * @return array<string, mixed>
     */
    public function summary(array $pack): array
    {
        $authored = $pack['versions'][0]['authored'];

        return [
            'id' => $pack['id'],
            'name' => $authored['name'],
            'authors' => $authored['authors'] ?? [],
            'abstract' => $authored['abstract'] ?? null,
            'tags' => $authored['tags'] ?? [],
            'icon' => $authored['images']['icon']['url'] ?? null,
            'version' => $authored['version'],
            'game_min' => $authored['compatibility']['game_min'] ?? null,
            'mod_count' => count($authored['mods'] ?? []),
            'updated_at' => $pack['updated_at'] ?? null,
            'updated_ago' => isset($pack['updated_at']) ? Carbon::parse($pack['updated_at'])->diffForHumans() : null,
        ];
    }
}
