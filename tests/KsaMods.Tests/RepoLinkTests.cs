using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using KsaMods.Api.Data;
using KsaMods.Forge;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Connecting a repository, over real HTTP against a real database and a forge that answers from
/// memory.
///
/// <para>The claim being tested is the one the whole listing model rests on: a repository link is
/// the ownership proof, so anybody able to connect a repository they do not control can publish
/// under somebody else's name. Before the challenge existed this endpoint stored whatever it was
/// told, and the only thing standing in the way was that nobody had tried.</para>
/// </summary>
public sealed class RepoLinkTests : IAsyncLifetime
{
    private ApiFactory? _factory;
    private NpgsqlDataSource? _source;
    private Database? _database;
    private readonly FakeForge _forge = new();

    private readonly List<long> _accounts = [];
    private readonly List<string> _mods = [];

    private Database Db => _database ?? throw new InvalidOperationException("No test database.");

    public Task InitializeAsync()
    {
        if (RequiresPostgresFactAttribute.Available)
        {
            _source = Database.CreateDataSource(RequiresPostgresFactAttribute.ConnectionString!);
            _database = new Database(_source);
            _factory = new ApiFactory(RequiresPostgresFactAttribute.ConnectionString!, _forge);
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            using var connection = await _database.OpenAsync(CancellationToken.None);

            await connection.ExecuteAsync(
                "delete from job where payload ->> 'modId' = any(@mods)", new { mods = _mods.ToArray() });
            await connection.ExecuteAsync(
                "delete from mod where id = any(@mods)", new { mods = _mods.ToArray() });
            await connection.ExecuteAsync(
                "delete from account where id = any(@accounts)", new { accounts = _accounts.ToArray() });
        }

        _factory?.Dispose();
        if (_source is not null) await _source.DisposeAsync();
    }

    [RequiresPostgresFact]
    public async Task Connecting_a_repository_does_not_connect_it_yet()
    {
        var (client, modId) = await OwnedModAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/mods/{modId}/repo-link",
            new { provider = "github", repoFullName = "someone/TheirMod" });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("verified").GetBoolean());
        Assert.StartsWith("ksamods-verify-", body.GetProperty("challenge").GetString(), StringComparison.Ordinal);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Null(await connection.ExecuteScalarAsync<DateTime?>(
            "select verified_at from repo_link where mod_id = @modId", new { modId }));
    }

    [RequiresPostgresFact]
    public async Task An_unproven_link_cannot_import_anything()
    {
        // The whole point. Claiming a repository is free; importing from it is what publishes
        // somebody else's work under your listing.
        var (client, modId) = await OwnedModAsync();

        using (var claim = await client.PostAsJsonAsync(
            $"/api/v1/mods/{modId}/repo-link",
            new { provider = "github", repoFullName = "someone/TheirMod" }))
        {
            claim.EnsureSuccessStatusCode();
        }

        using var import = await client.PostAsync($"/api/v1/mods/{modId}/releases/import", null);

        Assert.Equal(HttpStatusCode.Conflict, import.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "select count(*) from job where payload ->> 'modId' = @modId", new { modId }));
    }

    [RequiresPostgresFact]
    public async Task Publishing_the_challenge_proves_the_claim()
    {
        var (client, modId) = await OwnedModAsync();

        string challenge;

        using (var claim = await client.PostAsJsonAsync(
            $"/api/v1/mods/{modId}/repo-link",
            new { provider = "github", repoFullName = "someone/TheirMod" }))
        {
            claim.EnsureSuccessStatusCode();
            var body = await claim.Content.ReadFromJsonAsync<JsonElement>();
            challenge = body.GetProperty("challenge").GetString()!;
        }

        // Only somebody who can write to the repository can do this, which is the claim.
        _forge.VerificationFiles["someone/TheirMod"] = challenge;

        using (var verify = await client.PostAsync($"/api/v1/mods/{modId}/repo-link/verify", null))
        {
            verify.EnsureSuccessStatusCode();
        }

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.NotNull(await connection.ExecuteScalarAsync<DateTime?>(
            "select verified_at from repo_link where mod_id = @modId", new { modId }));

        // The challenge is cleared once spent, so a leaked deploy log cannot be replayed.
        Assert.Null(await connection.ExecuteScalarAsync<string?>(
            "select challenge from repo_link where mod_id = @modId", new { modId }));

        using var import = await client.PostAsync($"/api/v1/mods/{modId}/releases/import", null);
        Assert.Equal(HttpStatusCode.Accepted, import.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task The_wrong_file_contents_prove_nothing()
    {
        var (client, modId) = await OwnedModAsync();

        using (var claim = await client.PostAsJsonAsync(
            $"/api/v1/mods/{modId}/repo-link",
            new { provider = "github", repoFullName = "someone/TheirMod" }))
        {
            claim.EnsureSuccessStatusCode();
        }

        // A file with somebody else's challenge in it, which is what an attacker who saw one
        // would have.
        _forge.VerificationFiles["someone/TheirMod"] = RepositoryProof.NewChallenge();

        using var verify = await client.PostAsync($"/api/v1/mods/{modId}/repo-link/verify", null);

        Assert.Equal(HttpStatusCode.Conflict, verify.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task A_proven_link_cannot_be_taken_by_a_second_listing()
    {
        // Two listings claiming one repository is a dispute, and the proven claim wins it.
        var (mine, mineId) = await OwnedModAsync();
        var (theirs, theirsId) = await OwnedModAsync();

        string challenge;

        using (var claim = await mine.PostAsJsonAsync(
            $"/api/v1/mods/{mineId}/repo-link",
            new { provider = "github", repoFullName = "someone/TheirMod" }))
        {
            var body = await claim.Content.ReadFromJsonAsync<JsonElement>();
            challenge = body.GetProperty("challenge").GetString()!;
        }

        _forge.VerificationFiles["someone/TheirMod"] = challenge;

        using (var verify = await mine.PostAsync($"/api/v1/mods/{mineId}/repo-link/verify", null))
        {
            verify.EnsureSuccessStatusCode();
        }

        using var stolen = await theirs.PostAsJsonAsync(
            $"/api/v1/mods/{theirsId}/repo-link",
            new { provider = "github", repoFullName = "someone/TheirMod" });

        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Equal(mineId, await connection.ExecuteScalarAsync<string>(
            "select mod_id from repo_link where repo_full_name = 'someone/TheirMod'"));
    }

    [RequiresPostgresFact]
    public async Task An_unproven_claim_does_not_hold_a_repository_hostage()
    {
        // The other half of the same rule. If claiming were enough to block, anyone could park on
        // every repository they liked and nobody could ever connect one.
        var (squatter, squatterId) = await OwnedModAsync();
        var (owner, ownerId) = await OwnedModAsync();

        using (var claim = await squatter.PostAsJsonAsync(
            $"/api/v1/mods/{squatterId}/repo-link",
            new { provider = "github", repoFullName = "someone/TheirMod" }))
        {
            claim.EnsureSuccessStatusCode();
        }

        using var second = await owner.PostAsJsonAsync(
            $"/api/v1/mods/{ownerId}/repo-link",
            new { provider = "github", repoFullName = "someone/TheirMod" });

        second.EnsureSuccessStatusCode();

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Equal(ownerId, await connection.ExecuteScalarAsync<string>(
            "select mod_id from repo_link where repo_full_name = 'someone/TheirMod'"));
    }

    [RequiresPostgresFact]
    public async Task Re_pointing_a_verified_link_drops_the_proof_with_it()
    {
        // Otherwise a proven link becomes a way to publish from any repository at all: verify one
        // you own, then swap the target.
        var (client, modId) = await OwnedModAsync();

        string challenge;

        using (var claim = await client.PostAsJsonAsync(
            $"/api/v1/mods/{modId}/repo-link",
            new { provider = "github", repoFullName = "someone/TheirMod" }))
        {
            var body = await claim.Content.ReadFromJsonAsync<JsonElement>();
            challenge = body.GetProperty("challenge").GetString()!;
        }

        _forge.VerificationFiles["someone/TheirMod"] = challenge;

        using (var verify = await client.PostAsync($"/api/v1/mods/{modId}/repo-link/verify", null))
        {
            verify.EnsureSuccessStatusCode();
        }

        using (var repoint = await client.PostAsJsonAsync(
            $"/api/v1/mods/{modId}/repo-link",
            new { provider = "github", repoFullName = "victim/ValuableMod" }))
        {
            repoint.EnsureSuccessStatusCode();
        }

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Null(await connection.ExecuteScalarAsync<DateTime?>(
            "select verified_at from repo_link where mod_id = @modId", new { modId }));
    }

    [RequiresPostgresFact]
    public async Task A_forge_without_an_adapter_is_refused()
    {
        var (client, modId) = await OwnedModAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/mods/{modId}/repo-link",
            new { provider = "gitlab", repoFullName = "someone/TheirMod" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task A_repository_the_forge_has_never_heard_of_is_refused_before_anything_is_written()
    {
        // A typo caught here is a sentence. Caught later it is an import job that fails five times
        // and lands in the dead queue.
        var (client, modId) = await OwnedModAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/mods/{modId}/repo-link",
            new { provider = "github", repoFullName = "nobody/NoSuchThing" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "select count(*) from repo_link where mod_id = @modId", new { modId }));
    }

    // ── helpers ──

    private async Task<(HttpClient Client, string ModId)> OwnedModAsync()
    {
        var account = await NewAccountAsync();
        var sessions = new KsaMods.Api.Auth.SessionStore(Db, new KsaMods.Api.Auth.SiteSessionOptions());
        var (session, _) = await sessions.IssueAsync(account, "test", null, default);

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"ksamods_session={session}");

        return (client, await NewModAsync(account));
    }

    private async Task<long> NewAccountAsync()
    {
        using var connection = await Db.OpenAsync(CancellationToken.None);

        var id = await connection.ExecuteScalarAsync<long>("""
            insert into account (handle, display_name) values (@handle, 'Test Person') returning id
            """,
            new { handle = $"t{Guid.NewGuid():N}"[..16] });

        _accounts.Add(id);
        return id;
    }

    private async Task<string> NewModAsync(long owner)
    {
        var id = $"test.link.{Guid.NewGuid():N}"[..22];

        using var connection = await Db.OpenAsync(CancellationToken.None);

        await connection.ExecuteAsync("""
            insert into mod (id, id_lower, name, abstract, license, created_by)
            values (@id, lower(@id), 'Test mod', 'For tests.', 'MIT', @owner);

            insert into mod_maintainer (mod_id, account_id, role) values (@id, @owner, 'owner');
            """,
            new { id, owner });

        _mods.Add(id);
        return id;
    }

    /// <summary>
    /// A forge that answers from a dictionary. Verifying a challenge against the real GitHub would
    /// mean committing a file to a repository it knows about, which is not a test.
    /// </summary>
    private sealed class FakeForge : IForge
    {
        public string Provider => "github";

        public Dictionary<string, string> VerificationFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Known { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "someone/TheirMod",
            "victim/ValuableMod",
        };

        public Task<ForgeRepository> GetRepositoryAsync(string fullName, CancellationToken ct) =>
            Known.Contains(fullName)
                ? Task.FromResult(new ForgeRepository
                {
                    Id = fullName.GetHashCode(StringComparison.Ordinal).ToString(),
                    FullName = fullName,
                    DefaultBranch = "main",
                })
                : throw new ForgeException($"No repository at github.com/{fullName}, or it is private.");

        public Task<IReadOnlyList<ForgeRelease>> ListReleasesAsync(string fullName, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ForgeRelease>>([]);

        public Task<string?> ReadVerificationFileAsync(string fullName, string path, CancellationToken ct) =>
            Task.FromResult(VerificationFiles.GetValueOrDefault(fullName));
    }

    private sealed class StubFactory(IForge forge) : IForgeFactory
    {
        public IForge For(string provider) => forge;
    }

    private sealed class ApiFactory(string connectionString, IForge forge) : WebApplicationFactory<Database>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IForgeFactory>(new StubFactory(forge));
            });
        }
    }
}
