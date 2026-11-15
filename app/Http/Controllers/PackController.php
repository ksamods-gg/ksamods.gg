<?php

namespace App\Http\Controllers;

use App\Support\ContentIndex;
use Illuminate\Support\Carbon;
use Illuminate\Support\Collection;
use Inertia\Inertia;
use Inertia\Response;

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
