<?php

use App\Enums\Role;
use App\Models\Share;
use App\Models\ShareRelease;
use App\Models\ShareUpload;
use App\Models\User;
use App\Support\ContentIndex;
use App\Support\Share\Attestation;
use App\Support\Share\Signer;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\Client\Request as HttpRequest;
use Illuminate\Http\UploadedFile;
use Illuminate\Support\Facades\Artisan;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Storage;
use Illuminate\Testing\TestResponse;
use Inertia\Testing\AssertableInertia as Assert;
use Laravel\Passport\Client;
use Laravel\Passport\Passport;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();
    Storage::fake('public');

    // Passport's own keys, which signed-in API requests need.
    if (! file_exists(storage_path('oauth-private.key'))) {
        Artisan::call('passport:keys');
    }

    config([
        'share.disk' => 'public',
        'share.public_url' => 'https://files.test/',
        'share.signing_key' => Signer::generate(),
        'share.bot_token' => 'bot-token',
        'share.authored_repository' => 'ksamods-test/content-index',
    ]);

    // The pull requests the bot opened, and the files of each push.
    $this->pulls = [];
    $this->trees = [];
    $this->openBranch = null;

    Http::fake(function (HttpRequest $request) {
        $url = $request->url();

        if ($url === ContentIndex::URL) {
            return Http::response(['tags' => [], 'listings' => [
                ['id' => 'PartsPack', 'published_at' => '2026-01-01T00:00:00Z', 'updated_at' => '2026-01-01T00:00:00Z', 'authored' => ['type' => 'mod', 'name' => 'Parts Pack'], 'releases' => []],
                ['id' => 'StarMap', 'published_at' => '2026-01-01T00:00:00Z', 'updated_at' => '2026-01-01T00:00:00Z', 'authored' => ['type' => 'mod-loader', 'name' => 'StarMap'], 'releases' => []],
            ]]);
        }

        if (str_contains($url, 'snapshot-')) {
            return Http::response(status: 404);
        }

        $path = parse_url($url, PHP_URL_PATH);

        return match (true) {
            $path === '/repos/ksamods-test/content-index/forks' => Http::response(['full_name' => 'ksamods-bot/content-index'], 202),
            str_ends_with($path, '/merge-upstream') => Http::response([], 200),
            $path === '/repos/ksamods-test/content-index/git/ref/heads/main' => Http::response(['object' => ['sha' => 'main-sha']]),
            str_starts_with($path, '/repos/ksamods-bot/content-index/git/ref/heads/') => Http::response(['object' => ['sha' => 'branch-sha']]),
            str_starts_with($path, '/repos/ksamods-bot/content-index/git/commits/') => Http::response(['tree' => ['sha' => 'tree-sha']]),
            $path === '/repos/ksamods-bot/content-index/git/trees' => (function () use ($request) {
                $this->trees[] = $request['tree'];

                return Http::response(['sha' => 'new-tree']);
            })(),
            $path === '/repos/ksamods-bot/content-index/git/commits' => Http::response(['sha' => 'new-commit']),
            $path === '/repos/ksamods-bot/content-index/git/refs' => Http::response([], 201),
            str_starts_with($path, '/repos/ksamods-bot/content-index/git/refs/heads/') => Http::response([]),
            $path === '/repos/ksamods-test/content-index/pulls' && $request->method() === 'POST' => (function () use ($request) {
                $this->pulls[] = $request->data();
                $this->openBranch = $request['head'];

                return Http::response(['html_url' => 'https://github.com/ksamods-test/content-index/pull/'.count($this->pulls)], 201);
            })(),
            // Open pull requests: by head, the listing's own; otherwise none that add ids.
            $path === '/repos/ksamods-test/content-index/pulls' => Http::response(
                ($request->data()['head'] ?? null) !== null && $request->data()['head'] === $this->openBranch ? [['number' => 1]] : []
            ),
            default => Http::response(['message' => "unexpected {$request->method()} {$url}"], 500),
        };
    });
});

/**
 * A vehicle archive, as an upload.
 */
function vehicleUpload(string $folder = 'Example Rover'): UploadedFile
{
    $file = tempnam(sys_get_temp_dir(), 'share');
    $zip = new ZipArchive;
    $zip->open($file, ZipArchive::OVERWRITE);
    $zip->addFromString("{$folder}/meta.toml", "name = \"{$folder}\"\nversion = \"2026.10.10.5554\"\n");
    $zip->addFromString("{$folder}/vehicle.xml", '<Vehicle/>');
    $zip->close();

    return UploadedFile::fake()->createWithContent('vehicle.zip', (string) file_get_contents($file));
}

const NEEDS_PARTS = "[[dependencies]]\nid = \"PartsPack\"\nkind = \"required\"\nmin = \"1.4.0\"\n";

/**
 * Uploads and publishes a new vehicle through the API, as a signed-in client.
 *
 * @param  array<string, mixed>  $listing
 */
function publishVehicle(User $user, array $listing = [], string $dependencies = NEEDS_PARTS): TestResponse
{
    Passport::actingAs($user);
    $upload = test()->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload(), 'dependencies' => $dependencies])->assertCreated()->json('upload');

    return test()->postJson("/api/share/v1/uploads/{$upload}/publish", [
        'version' => '1.0.0',
        'status' => 'stable',
        'id' => 'ExampleRover',
        'name' => 'Example Rover',
        'authors' => ['Maxi'],
        'abstract' => 'A six-wheel rover for the Moon.',
        'license' => 'CC-BY-4.0',
        ...$listing,
    ]);
}

/**
 * A data file of the share disk, after checking its signature as the index does (rule 22).
 *
 * @return array<string, mixed>
 */
function signedFile(string $path): array
{
    $bytes = Storage::disk('public')->get($path);
    $signature = Storage::disk('public')->get("{$path}.sig");

    expect($bytes)->not->toBeNull()
        ->and(Signer::verify((string) $bytes, (string) $signature, Signer::publicKey()))->toBeTrue()
        ->and(str_starts_with((string) $bytes, "\xEF\xBB\xBF"))->toBeFalse();

    return json_decode((string) $bytes, true);
}

describe('the upload API', function () {
    it('checks an upload and answers with its checks, a proposed version and when it expires', function () {
        Passport::actingAs(User::factory()->create());

        $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload(), 'dependencies' => NEEDS_PARTS])
            ->assertCreated()
            ->assertJsonStructure(['upload', 'checks' => [['name', 'passed', 'message']], 'version', 'listing', 'expires'])
            ->assertJsonPath('version', '1.0.0')
            ->assertJsonPath('type', 'vehicle')
            ->assertJson(fn ($json) => $json->where('expires', fn (string $expires) => now()->addDay()->lte($expires))->etc());

        // Kept under uploads/, outside the archives/ prefix that downloads names, until it is published.
        expect(ShareUpload::sole())->passed->toBeTrue()->folder->toBe('Example Rover')->path->toStartWith('uploads/');
    });

    it('publishes a new vehicle in the order of rule 9, and opens its pull request from the bot', function () {
        $user = User::factory()->create(['github_id' => '777']);

        publishVehicle($user)->assertCreated()->assertExactJson([
            'id' => 'ExampleRover',
            'type' => 'vehicle',
            'version' => '1.0.0',
            'pull_request' => 'https://github.com/ksamods-test/content-index/pull/1',
        ]);

        $share = Share::sole();
        $release = ShareRelease::sole();
        Storage::disk('public')->assertExists('archives/vehicle/ExampleRover/1.0.0/ExampleRover-1.0.0.zip');
        expect(Storage::disk('public')->getVisibility($release->path))->toBe('public');

        // The owner record and the release list, signed.
        expect(signedFile('owners/vehicle/ExampleRover.json'))->toBe([
            'id' => 'ExampleRover', 'type' => 'vehicle', 'account' => $user->fresh()->share_account, 'github_id' => 777,
        ]);
        expect(signedFile('releases/vehicle/ExampleRover.json'))->toBe([
            'id' => 'ExampleRover',
            'type' => 'vehicle',
            'releases' => [[
                'version' => '1.0.0',
                'status' => 'stable',
                'url' => route('shares.download', ['type' => 'vehicle', 'id' => 'ExampleRover', 'version' => '1.0.0']),
                'date' => $release->created_at->utc()->format('Y-m-d\TH:i:s\Z'),
            ]],
        ]);

        // The pull request carries exactly the attested change: vehicles/ExampleRover.toml.
        [$file] = $this->trees[0];
        expect($file['path'])->toBe('vehicles/ExampleRover.toml')
            ->and($file['content'])->toContain('ksamods = "ExampleRover"')
            ->and($file['content'])->toContain('homepage = "'.route('shares.show', ['types' => 'vehicles', 'id' => 'ExampleRover']).'"')
            ->and($file['content'])->toContain("[[dependencies]]\nid = \"PartsPack\"\nkind = \"required\"\nmin = \"1.4.0\"");

        $digest = Attestation::digest(['vehicles/ExampleRover.toml' => $file['content']]);
        expect(signedFile("attestations/{$digest}.json"))
            ->toMatchArray(['host' => 'ksamods', 'accounts' => ['vehicle/ExampleRover' => $share->account]]);

        expect($this->pulls[0])->toMatchArray(['base' => 'main'])
            ->and($this->pulls[0]['head'])->toStartWith('ksamods-bot:share/ExampleRover-')
            ->and($share->pull_request_url)->toBe('https://github.com/ksamods-test/content-index/pull/1');

        // The account id in owner records says nothing about the person.
        expect($share->account)->toMatch('/^u_[A-Za-z0-9]{12}$/');
    });

    it('adds a new version without a pull request, unless the dependencies changed', function () {
        $user = User::factory()->create();
        publishVehicle($user)->assertCreated();

        $upload = $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload(), 'dependencies' => NEEDS_PARTS, 'listing' => 'ExampleRover'])
            ->assertCreated()->assertJsonPath('version', '1.1.0')->json('upload');
        $this->postJson("/api/share/v1/uploads/{$upload}/publish", ['version' => '1.1.0', 'status' => 'testing'])
            ->assertCreated()->assertJsonPath('pull_request', null);

        expect(signedFile('releases/vehicle/ExampleRover.json')['releases'])->toHaveCount(2)
            ->and(count($this->pulls))->toBe(1);

        // New dependencies: the listing changes, pushed to its open pull request with a new attestation.
        $upload = $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload(), 'dependencies' => '', 'listing' => 'ExampleRover'])->json('upload');
        $this->postJson("/api/share/v1/uploads/{$upload}/publish", ['version' => '1.2.0'])
            ->assertCreated()->assertJsonPath('pull_request', 'https://github.com/ksamods-test/content-index/pull/1');

        expect(count($this->pulls))->toBe(1)->and($this->trees)->toHaveCount(2);
        $digest = Attestation::digest(['vehicles/ExampleRover.toml' => $this->trees[1][0]['content']]);
        Storage::disk('public')->assertExists("attestations/{$digest}.json");
    });

    it('answers each refusal with the error code of rule 16', function () {
        $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload()])
            ->assertUnauthorized()->assertJson(['error' => 'unauthorized']);

        $owner = User::factory()->create();
        publishVehicle($owner)->assertCreated();

        $other = User::factory()->create();
        Passport::actingAs($other);

        $this->postJson('/api/share/v1/uploads', ['archive' => UploadedFile::fake()->create('rover.zip', 10)])
            ->assertUnprocessable()->assertJson(['error' => 'invalid_archive'])->assertJsonStructure(['checks']);
        $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload(), 'listing' => 'ExampleRover'])
            ->assertForbidden()->assertJson(['error' => 'not_owner']);
        // The folder is taken by ExampleRover.
        $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload()])
            ->assertUnprocessable()->assertJson(['error' => 'invalid_archive']);

        $upload = fn () => $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload('Other Rover')])->json('upload');
        $publish = fn (string $upload, array $listing) => $this->postJson("/api/share/v1/uploads/{$upload}/publish", [
            'version' => '1.0.0', 'name' => 'Other', 'authors' => ['Val'], 'abstract' => 'Another.', 'license' => 'MIT', ...$listing,
        ]);

        $publish($upload(), ['id' => 'PartsPack'])->assertConflict()->assertJson(['error' => 'id_taken']);
        $publish($upload(), ['id' => 'exampleROVER'])->assertConflict()->assertJson(['error' => 'id_taken']);

        $this->travel(3)->days();
        $expired = $upload();
        $this->travel(3)->days();
        $publish($expired, ['id' => 'OtherRover'])->assertStatus(410)->assertJson(['error' => 'upload_expired']);

        $mine = $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload('Example Rover'), 'listing' => 'ExampleRover']);
        $mine->assertForbidden();

        Passport::actingAs($owner);
        $again = $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload(), 'listing' => 'ExampleRover'])->json('upload');
        $this->postJson("/api/share/v1/uploads/{$again}/publish", ['version' => '1.0.0'])
            ->assertConflict()->assertJson(['error' => 'version_taken']);
    });

    it('limits how many new ids an account claims a day', function () {
        config(['share.new_listings_per_day' => 1]);
        $user = User::factory()->create();
        publishVehicle($user)->assertCreated();

        $upload = $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload('Other Rover')])->json('upload');
        $this->postJson("/api/share/v1/uploads/{$upload}/publish", [
            'version' => '1.0.0', 'id' => 'OtherRover', 'name' => 'Other', 'authors' => ['Val'], 'abstract' => 'Another.', 'license' => 'MIT',
        ])->assertTooManyRequests()->assertJson(['error' => 'rate_limited'])->assertJson(fn ($json) => $json->whereType('retry_after', 'integer')->etc());
    });

    it('refuses a dependency on something other than a mod', function () {
        Passport::actingAs(User::factory()->create());

        $this->postJson('/api/share/v1/uploads', ['archive' => vehicleUpload(), 'dependencies' => "[[dependencies]]\nid = \"StarMap\"\nkind = \"required\""])
            ->assertUnprocessable()->assertJson(['error' => 'invalid_request']);
    });
});

describe('the device sign-in', function () {
    it('takes any client_id as a public client, and starts RFC 8628\'s flow', function () {
        $this->post('/api/share/v1/oauth/device_authorization', ['client_id' => 'borea'])
            ->assertOk()
            ->assertJsonStructure(['device_code', 'user_code', 'verification_uri', 'expires_in']);

        $client = Client::sole();
        expect($client->name)->toBe('device:borea')->and($client->confidential())->toBeFalse();

        // The same name reuses its client.
        $this->post('/api/share/v1/oauth/device_authorization', ['client_id' => 'borea'])->assertOk();
        expect(Client::count())->toBe(1);

        $this->post('/api/share/v1/oauth/device_authorization', ['client_id' => 'not a name'])
            ->assertStatus(400)->assertJson(['error' => 'invalid_client']);
    });

    it('signs a client in after the player approves, and lets it refresh its token by its name', function () {
        $start = $this->post('/api/share/v1/oauth/device_authorization', ['client_id' => 'borea'])->json();
        $grant = ['grant_type' => 'urn:ietf:params:oauth:grant-type:device_code', 'client_id' => 'borea', 'device_code' => $start['device_code']];

        expect($start['interval'])->toBe(5)
            ->and($start['verification_uri_complete'])->toContain($start['user_code']);
        $this->post('/api/share/v1/oauth/token', $grant)->assertStatus(400)->assertJson(['error' => 'authorization_pending']);

        $user = User::factory()->create();
        $this->actingAs($user)->get('/oauth/device/authorize?user_code='.$start['user_code']);
        $this->actingAs($user)->post('/oauth/device/authorize', ['auth_token' => session('authToken')])->assertRedirect();

        $this->travel(6)->seconds();
        $tokens = $this->post('/api/share/v1/oauth/token', $grant)->assertOk()->json();
        expect($tokens)->toHaveKeys(['access_token', 'refresh_token', 'expires_in'])
            ->and($user->fresh()->share_consented_at)->not->toBeNull();

        $this->post('/api/share/v1/oauth/token', ['grant_type' => 'refresh_token', 'client_id' => 'borea', 'refresh_token' => $tokens['refresh_token']])
            ->assertOk()->assertJsonStructure(['access_token', 'refresh_token']);
    });

    it('shows the client\'s name when the player approves it', function () {
        $code = $this->post('/api/share/v1/oauth/device_authorization', ['client_id' => 'borea'])->json('user_code');

        $this->actingAs(User::factory()->create())
            ->get('/oauth/device/authorize?user_code='.$code)
            ->assertInertia(fn (Assert $page) => $page->component('Device/Authorize')->where('client', 'borea'));
    });
});

describe('the website', function () {
    it('asks for consent before the first upload, then checks and publishes like the API', function () {
        $user = User::factory()->create();

        $this->actingAs($user)->post(route('shares.upload'), ['archive' => vehicleUpload()])
            ->assertSessionHasErrors('consent');

        $response = $this->actingAs($user)->post(route('shares.upload'), [
            'archive' => vehicleUpload(),
            'consent' => '1',
            'dependencies' => [['id' => 'PartsPack', 'kind' => 'required', 'min' => '1.4.0']],
        ]);
        $upload = ShareUpload::sole();
        $response->assertRedirect(route('shares.review', $upload));
        expect($user->fresh()->share_consented_at)->not->toBeNull()
            ->and($upload->dependencies)->toBe([['id' => 'PartsPack', 'kind' => 'required', 'min' => '1.4.0']]);

        $this->actingAs($user)->get(route('shares.review', $upload))
            ->assertInertia(fn (Assert $page) => $page->component('Shares/Review')->where('version', '1.0.0')->where('defaults.name', 'Example Rover'));

        $this->actingAs($user)->post(route('shares.publish', $upload), [
            'version' => '1.0.0', 'status' => 'stable', 'id' => 'ExampleRover', 'name' => 'Example Rover',
            'authors' => 'Maxi, Jeb', 'abstract' => 'A rover.', 'license' => 'CC-BY-4.0', 'tags' => 'rover',
        ])->assertRedirect(route('shares.show', ['types' => 'vehicles', 'id' => 'ExampleRover']));

        expect(Share::sole()->authors)->toBe(['Maxi', 'Jeb']);

        $this->get(route('shares.index', 'vehicles'))
            ->assertInertia(fn (Assert $page) => $page->component('Shares/Index')->where('shares.data.0.id', 'ExampleRover'));
        $this->get(route('shares.show', ['types' => 'vehicles', 'id' => 'ExampleRover']))
            ->assertInertia(fn (Assert $page) => $page->component('Shares/Show')
                ->where('share.dependencies.0.name', 'Parts Pack')
                ->where('share.releases.0.version', '1.0.0'));
    });

    it('shows a failed check on the upload page', function () {
        $user = User::factory()->create(['share_consented_at' => now()]);

        $this->actingAs($user)->post(route('shares.upload'), ['archive' => UploadedFile::fake()->create('rover.zip', 10)])
            ->assertSessionHasErrors(['archive', 'checks']);
    });
});

it('redirects a release url to its archive, and answers 410 once a moderator takes it down', function () {
    publishVehicle(User::factory()->create())->assertCreated();
    $url = route('shares.download', ['type' => 'vehicle', 'id' => 'ExampleRover', 'version' => '1.0.0']);
    $release = ShareRelease::sole();

    $this->get($url)->assertRedirect('https://files.test/archives/vehicle/ExampleRover/1.0.0/ExampleRover-1.0.0.zip');

    $this->actingAs(User::factory()->create())->post(route('shares.take-down', $release))->assertForbidden();
    $this->actingAs(User::factory()->create(['role' => Role::Moderator]))->post(route('shares.take-down', $release))->assertRedirect();

    $this->get($url)->assertStatus(410);
    Storage::disk('public')->assertMissing($release->path);
    expect(signedFile('releases/vehicle/ExampleRover.json')['releases'])->toBe([]);
});

it('rewrites the owner record when the owner links or unlinks GitHub, or deletes their account', function () {
    $user = User::factory()->create(['github_id' => '777']);
    publishVehicle($user)->assertCreated();
    expect(signedFile('owners/vehicle/ExampleRover.json'))->toHaveKey('github_id', 777);

    $user->forceFill(['github_id' => null])->save();
    expect(signedFile('owners/vehicle/ExampleRover.json'))->not->toHaveKey('github_id');

    $user->forceFill(['github_id' => '888'])->save();
    expect(signedFile('owners/vehicle/ExampleRover.json'))->toHaveKey('github_id', 888);

    $account = $user->share_account;
    $user->delete();
    expect(signedFile('owners/vehicle/ExampleRover.json'))->toBe(['id' => 'ExampleRover', 'type' => 'vehicle', 'account' => $account]);
});

it('prints the hosts.toml table that registers this host, laid out as hosts.toml writes it', function () {
    config(['share.bot_id' => 4242, 'app.url' => 'https://ksamods.gg']);

    expect(Artisan::call('share:host'))->toBe(0)
        ->and(trim(Artisan::output()))->toBe(implode("\n", [
            '[ksamods]',
            'kind = "share"',
            'name = "ksamods.gg"',
            'data = "https://files.test/"',
            'downloads = ["https://ksamods.gg/share/files/", "https://files.test/archives/"]',
            'key = "'.Signer::publicKey().'"',
            'retired_keys = []',
            'bot = 4242',
            'api = "https://ksamods.gg/api/share/v1/"',
            'types = ["vehicle", "save"]',
            'max_bytes = { vehicle = 10485760, save = 104857600 }',
            'new_listings_per_day = 5',
            'policy = "https://ksamods.gg/policy"',
        ]));
});

it('refuses to print a table hosts.toml would reject', function () {
    config(['share.bot_id' => 0, 'app.url' => 'http://localhost']);

    expect(Artisan::call('share:host'))->toBe(1)
        ->and(Artisan::output())->toContain('SHARE_BOT_ID')->toContain('APP_URL must be https');
});
