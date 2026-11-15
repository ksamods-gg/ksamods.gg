<?php

use App\Support\PackRules;
use Devium\Toml\Toml;

// The pack member rules of the content index, in its own cases (schemas/pack-vectors.json, copied
// into tests/fixtures). A pack takes only mods for now, so cases that pin vehicles or saves wait.
it('follows the member rules for every mod case in content-index\'s pack vectors', function (array $case) {
    $pack = Toml::decode($case['pack'], asArray: true);
    $rules = PackRules::fromSnapshot($case['snapshot']);
    $found = $rules->check($pack['mods'] ?? []);

    expect($found['errors'])->toBe($case['errors'])
        ->and($found['notes'])->toBe($case['notes'])
        ->and($found['errors'] === [])->toBe($case['accepted']);

    if (isset($case['adds'])) {
        expect($rules->missing($pack['mods'] ?? [])['pins'])->toBe($case['adds']);
    }
})->with(fn () => collect(json_decode(file_get_contents(__DIR__.'/../fixtures/pack-vectors.json'), true)['vectors'])
    ->reject(fn (array $case) => isset($case['snapshot']['vehicles']) || isset($case['snapshot']['saves']) || preg_match('/^\[\[(vehicles|saves)\]\]/m', $case['pack']))
    ->mapWithKeys(fn (array $case) => [$case['name'] => [$case]])
    ->all());

it('compares versions as the index does', function () {
    expect(PackRules::compare('2.1.0-rc.1', '2.1.0'))->toBe(-1)
        ->and(PackRules::compare('0.5', '0.5.0'))->toBe(0)
        ->and(PackRules::compare('1.10.0', '1.9.0'))->toBe(1)
        ->and(PackRules::compare('1.0.0-alpha.2', '1.0.0-alpha.10'))->toBe(-1)
        ->and(PackRules::compare('nope', '1.0.0'))->toBeNull();
});
