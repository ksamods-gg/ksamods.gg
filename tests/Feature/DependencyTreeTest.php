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

it('shows everything that needs a mod, the mods that need those in turn', function () {
    $this->withoutVite();
    Http::fake([ContentIndex::URL => Http::response(['tags' => ['mod' => []], 'listings' => [
        treeListing('Rockets', [['id' => 'Engines', 'kind' => 'required']], loader: 'Loader'),
        // Fuel needs Rockets, and Engines needs Fuel: a cycle back to Engines.
        treeListing('Fuel', [['id' => 'Rockets', 'kind' => 'required']]),
        treeListing('Engines', [['id' => 'Fuel', 'kind' => 'required']], loader: 'Loader'),
        treeListing('Paint', [['any_of' => [['id' => 'Engines'], ['id' => 'Wheels']], 'kind' => 'required']]),
        treeListing('Loader', []),
    ]])]);

    $this->get(route('mods.show', 'Engines'))->assertInertia(fn (Assert $page) => $page
        ->has('dependentTree', 2)
        ->where('dependentTree.0.id', 'Rockets')
        ->where('dependentTree.0.children.0.id', 'Fuel')
        // Fuel is needed by Engines, the mod itself, shown at the top already.
        ->where('dependentTree.0.children.0.children.0.id', 'Engines')
        ->where('dependentTree.0.children.0.children.0.repeat', true)
        // Needing Engines as one of its alternatives counts too.
        ->where('dependentTree.1.id', 'Paint'));
});
