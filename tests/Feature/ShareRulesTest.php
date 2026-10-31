<?php

use App\Support\Share\Attestation;
use App\Support\Share\SaveName;
use App\Support\Share\ShareArchive;
use App\Support\Share\Signer;
use Illuminate\Support\Facades\Http;

beforeEach(function () {
    config(['share.signing_key' => Signer::generate()]);
    Http::fake();
});

/**
 * A zip with the given entries (path => contents), and the path of the file.
 *
 * @param  array<string, string>  $entries
 */
function shareZip(array $entries, ?Closure $tweak = null): string
{
    $file = tempnam(sys_get_temp_dir(), 'share');
    $zip = new ZipArchive;
    $zip->open($file, ZipArchive::OVERWRITE);

    foreach ($entries as $path => $contents) {
        $zip->addFromString($path, $contents);
    }

    $tweak?->__invoke($zip);
    $zip->close();

    return $file;
}

function rover(string $folder = 'Example Rover', string $root = 'Example Rover/', array $extra = []): string
{
    return shareZip([
        "{$root}meta.toml" => "name = \"{$folder}\"\nversion = \"2026.10.10.5554\"\n",
        "{$root}vehicle.xml" => '<Vehicle><Part id="1"/></Vehicle>',
        ...$extra,
    ]);
}

/**
 * @return array<string, bool>
 */
function verdicts(ShareArchive $inspection): array
{
    return collect($inspection->checks)->mapWithKeys(fn (array $check) => [$check['name'] => $check['passed']])->all();
}

it('follows the name rule for every case in content-index\'s vectors', function (string $value, bool $accepted) {
    expect(SaveName::isValid($value))->toBe($accepted);
})->with(fn () => collect(json_decode(file_get_contents(__DIR__.'/../fixtures/save-names.json'), true)['vectors'])
    ->mapWithKeys(fn (array $case) => [$case['name'] => [$case['value'], $case['accepted']]])
    ->all());

it('makes the canonical form and digest of every case in content-index\'s attestation vectors', function (array $files, string $canonical, string $sha256) {
    $blobs = [];

    foreach ($files as $file) {
        if (isset($file['previous_filename'])) {
            $blobs[$file['previous_filename']] = null;
        }

        $blobs[$file['filename']] = $file['status'] === 'removed' ? null : $file['sha'];
    }

    expect(Attestation::canonical($blobs))->toBe($canonical)
        ->and(hash('sha256', Attestation::canonical($blobs)))->toBe($sha256);
})->with(fn () => collect(json_decode(file_get_contents(__DIR__.'/../fixtures/attestation-vectors.json'), true)['vectors'])
    ->filter(fn (array $case) => isset($case['canonical']))
    ->mapWithKeys(fn (array $case) => [$case['name'] => [$case['files'], $case['canonical'], $case['sha256']]])
    ->all());

it('computes git blob ids as GitHub reports them', function () {
    // `git hash-object` of an empty file and of "hello\n".
    expect(Attestation::blob(''))->toBe('e69de29bb2d1d6434b8b29ae775ad8c2e48c5391')
        ->and(Attestation::blob("hello\n"))->toBe('ce013625030ba8dba906f756967f9e9ca394464a');
});

it('signs data files the way the index verifies them', function () {
    $body = "{\"id\": \"ExampleRover\"}\n";
    $signature = Signer::sign($body);
    $key = base64_decode(Signer::publicKey(), true);

    // A 44-byte Ed25519 SubjectPublicKeyInfo, and a canonical padded base64 signature of 64 bytes.
    expect(strlen($key))->toBe(44)
        ->and(bin2hex(substr($key, 0, 12)))->toBe('302a300506032b6570032100')
        ->and(base64_encode(base64_decode($signature, true)))->toBe($signature)
        ->and(strlen(base64_decode($signature, true)))->toBe(64)
        ->and(Signer::verify($body, $signature, Signer::publicKey()))->toBeTrue()
        ->and(Signer::verify($body.' ', $signature, Signer::publicKey()))->toBeFalse();
});

describe('archive checks', function () {
    it('accepts a vehicle in its folder, and reads its folder and game build', function () {
        $inspection = ShareArchive::inspect(rover(), 1000);

        expect($inspection->passed())->toBeTrue()
            ->and($inspection->type)->toBe('vehicle')
            ->and($inspection->folder)->toBe('Example Rover')
            ->and($inspection->gameVersion)->toBe('2026.10.10.5554')
            ->and(verdicts($inspection))->toMatchArray(['archive' => true, 'root' => true, 'type' => true, 'size' => true, 'meta.toml' => true, 'name' => true, 'version' => true, 'game file' => true]);
    });

    it('accepts the folder\'s files at the top of the archive, and a save', function () {
        expect(ShareArchive::inspect(rover(root: ''), 1000)->passed())->toBeTrue();

        $save = shareZip(['Moon Base/meta.toml' => "name = \"Moon Base\"\nversion = \"2026.10.10.5554\"", 'Moon Base/universe.xml' => '<Universe/>']);
        expect(ShareArchive::inspect($save, 1000))->type->toBe('save');
    });

    it('accepts the game build with the leading v the game writes, and drops it', function () {
        $vehicle = shareZip(['Rover/meta.toml' => "name = \"Rover\"\nversion = \"v2026.10.25.5707\"", 'Rover/vehicle.xml' => '<V/>']);

        expect(ShareArchive::inspect($vehicle, 1000))->gameVersion->toBe('2026.10.25.5707');
    });

    it('refuses an archive that fails a check, with the code of rule 16', function (Closure $archive, string $check, string $error, int $size = 1000, ?string $type = null) {
        $inspection = ShareArchive::inspect($archive(), $size, $type);

        expect($inspection->error)->toBe($error)
            ->and(collect($inspection->checks)->firstWhere('passed', false)['name'])->toBe($check);
    })->with([
        'not a zip' => [fn () => tap(tempnam(sys_get_temp_dir(), 'share'), fn ($file) => file_put_contents($file, 'nope')), 'archive', 'invalid_archive'],
        'a folder named otherwise' => [fn () => rover(folder: 'Example Rover', root: 'Rover/'), 'root', 'invalid_archive'],
        'a name the game refuses' => [fn () => rover(folder: 'Rover.v2', root: 'Rover.v2/'), 'name', 'invalid_archive'],
        'no meta.toml' => [fn () => shareZip(['Rover/vehicle.xml' => '<V/>']), 'root', 'invalid_archive'],
        'broken meta.toml' => [fn () => shareZip(['Rover/meta.toml' => 'name = ', 'Rover/vehicle.xml' => '<V/>']), 'meta.toml', 'invalid_archive'],
        'no game build' => [fn () => shareZip(['Rover/meta.toml' => 'name = "Rover"', 'Rover/vehicle.xml' => '<V/>']), 'version', 'invalid_archive'],
        'a game build with a suffix' => [fn () => shareZip(['Rover/meta.toml' => "name = \"Rover\"\nversion = \"2026.10.10.5554-beta\"", 'Rover/vehicle.xml' => '<V/>']), 'version', 'invalid_archive'],
        'broken xml' => [fn () => shareZip(['Rover/meta.toml' => "name = \"Rover\"\nversion = \"2026.10.10.5554\"", 'Rover/vehicle.xml' => '<V>']), 'game file', 'invalid_archive'],
        'both game files' => [fn () => rover(extra: ['Example Rover/universe.xml' => '<U/>']), 'root', 'invalid_archive'],
        'a parent path' => [fn () => rover(extra: ['Example Rover/../../evil' => 'x']), 'archive', 'invalid_archive'],
        'a symlink' => [fn () => shareZip(
            ['Rover/meta.toml' => "name = \"Rover\"\nversion = \"2026.10.10.5554\"", 'Rover/vehicle.xml' => '<V/>', 'Rover/link' => '/etc/passwd'],
            fn (ZipArchive $zip) => $zip->setExternalAttributesName('Rover/link', ZipArchive::OPSYS_UNIX, 0120777 << 16),
        ), 'archive', 'invalid_archive'],
        'too large for a vehicle' => [fn () => rover(), 'size', 'too_large', 10 * 1024 * 1024 + 1],
        'a save for a vehicle listing' => [fn () => shareZip(['Base/meta.toml' => "name = \"Base\"\nversion = \"2026.10.10.5554\"", 'Base/universe.xml' => '<U/>']), 'type', 'invalid_archive', 1000, 'vehicle'],
    ]);

    it('refuses a type the host does not take', function () {
        config(['share.types' => ['vehicle']]);
        $save = shareZip(['Base/meta.toml' => "name = \"Base\"\nversion = \"2026.10.10.5554\"", 'Base/universe.xml' => '<U/>']);

        expect(ShareArchive::inspect($save, 1000)->error)->toBe('type_not_accepted');
    });

    it('does not expand external entities in the game file', function () {
        $zip = shareZip([
            'Rover/meta.toml' => "name = \"Rover\"\nversion = \"2026.10.10.5554\"",
            'Rover/vehicle.xml' => '<?xml version="1.0"?><!DOCTYPE v [<!ENTITY x SYSTEM "http://127.0.0.1:1/">]><v>&x;</v>',
        ]);

        expect(ShareArchive::inspect($zip, 1000)->passed())->toBeTrue();
    });
});
