<?php

use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

// A listing whose one release has these dependencies (and loader).
function treeListing(string $id, array $dependencies, ?string $loader = null): array
{
    return [
        'id' => $id, 'published_at' => '2026-01-01T00:00:00Z', 'updated_at' => '2026-01-01T00:00:00Z',
        'authored' => ['type' => $id === 'Loader' ? 'mod-loader' : 'mod', 'name' => "{$id} mod"],
        'releases' => [array_filter([
            'version' => '1.0.0', 'release_status' => 'stable', 'release_date' => '2026-01-01T00:00:00Z',
            'download' => ['url' => "https://example.com/{$id}.zip"],
            'dependencies' => $dependencies,
            'loader' => $loader ? ['id' => $loader] : null,
        ])],
    ];
}

it('shows everything a mod pulls in, marking repeats and conflicts', function () {
    $this->withoutVite();
    Http::fake([ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => [
        treeListing('Rockets', [['id' => 'Engines', 'kind' => 'required'], ['id' => 'Paint', 'kind' => 'optional']], loader: 'Loader'),
        treeListing('Engines', [['id' => 'Fuel', 'kind' => 'required'], ['id' => 'Ghost', 'kind' => 'required']], loader: 'Loader'),
        // Fuel needs Rockets back (a cycle), and conflicts with the loader.
        treeListing('Fuel', [['id' => 'Rockets', 'kind' => 'required'], ['id' => 'Loader', 'kind' => 'conflict']]),
        treeListing('Loader', []),
        treeListing('Paint', []),
    ]])]);

    $this->get(route('mods.show', 'Rockets'))->assertInertia(fn (Assert $page) => $page
        ->where('dependencyTree.nodes.0.id', 'Loader')
        ->where('dependencyTree.nodes.0.kind', 'loader')
        ->where('dependencyTree.nodes.1.id', 'Engines')
        // Engines' loader was shown already, so it is not expanded again.
        ->where('dependencyTree.nodes.1.children.0.id', 'Loader')
        ->where('dependencyTree.nodes.1.children.0.repeat', true)
        ->where('dependencyTree.nodes.1.children.1.id', 'Fuel')
        ->where('dependencyTree.nodes.1.children.1.children.0.repeat', true)
        ->where('dependencyTree.nodes.1.children.2.listed', false)
        // Optional mods are not pulled in.
        ->has('dependencyTree.nodes', 2)
        ->where('dependencyTree.conflicts', [['a' => 'Fuel mod', 'b' => 'Loader mod']]));
});
