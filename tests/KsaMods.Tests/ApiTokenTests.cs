using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// API tokens, over real HTTP.
///
/// <para>The claim being tested is one sentence: <b>tokens read, sessions write</b>. It is enforced
/// centrally rather than per endpoint, which makes it exactly the kind of rule that looks correct
/// forever and fails silently the day somebody widens a helper. So the write refusal is checked
/// against real write endpoints rather than against the helper that implements it.</para>
/// </summary>
public sealed class ApiTokenTests : IAsyncLifetime
{
    private ApiFactory? _factory;
    private NpgsqlDataSource? _source;
    private Database? _database;

    private readonly List<long> _accounts = [];
    private readonly List<string> _mods = [];

    private Database Db => _database ?? throw new InvalidOperationException("No test database.");

    public Task InitializeAsync()
    {
        if (RequiresPostgresFactAttribute.Available)
        {
            _source = Database.CreateDataSource(RequiresPostgresFactAttribute.ConnectionString!);
            _database = new Database(_source);
            _factory = new ApiFactory(RequiresPostgresFactAttribute.ConnectionString!);
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            using var connection = await _database.OpenAsync(CancellationToken.None);

            await connection.ExecuteAsync("delete from mod where id = any(@mods)", new { mods = _mods.ToArray() });
            await connection.ExecuteAsync("delete from account where id = any(@ids)", new { ids = _accounts.ToArray() });
        }

        _factory?.Dispose();
        if (_source is not null) await _source.DisposeAsync();
    }

    // ── what a token may do ──────────────────────────────────────────────

    [RequiresPostgresFact]
    public async Task A_token_can_read_the_public_catalogue()
    {
        var (_, secret) = await TokenAsync();

        using var client = Bearer(secret);
        using var response = await client.GetAsync("/api/v1/mods");

        response.EnsureSuccessStatusCode();
    }

    [RequiresPostgresFact]
    public async Task A_personal_token_sees_its_own_draft()
    {
        // The reason to hold one: a script that watches your own listings, including the ones not
        // published yet. Anonymous callers get nothing for an unlisted mod.
        var (account, secret) = await TokenAsync();
        var mod = await DraftModAsync(account);

        using var withToken = Bearer(secret);
        using var mine = await withToken.GetAsync($"/api/v1/mods/{mod}");

        mine.EnsureSuccessStatusCode();

        using var anonymous = _factory!.CreateClient();
        using var theirs = await anonymous.GetAsync($"/api/v1/mods/{mod}");

        // Unlisted is link-reachable by design, so the listing resolves for anybody - what a token
        // adds is the maintainer's view of it.
        var body = await mine.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal("owner", document.RootElement.GetProperty("your_role").GetString());
    }

    [RequiresPostgresFact]
    public async Task An_application_token_never_claims_a_person()
    {
        // It identifies software. Handing it somebody's drafts would make "application key" a
        // second, quieter personal token.
        var (account, secret) = await TokenAsync(kind: "application");
        var mod = await DraftModAsync(account);

        using var client = Bearer(secret);
        using var response = await client.GetAsync($"/api/v1/mods/{mod}");

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(document.RootElement.GetProperty("your_role").ValueKind is JsonValueKind.Null);
    }

    // ── what a token may not do ──────────────────────────────────────────

    [RequiresPostgresFact]
    public async Task A_token_cannot_write_even_as_the_owner()
    {
        // Against a real write endpoint, not the helper that guards it: the rule is central, and a
        // central rule fails everywhere at once when it fails.
        var (account, secret) = await TokenAsync();
        var mod = await DraftModAsync(account);

        using var client = Bearer(secret);

        using var edit = await client.PatchAsJsonAsync($"/api/v1/mods/{mod}", new { name = "Renamed" });
        Assert.Equal(HttpStatusCode.Unauthorized, edit.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Equal("Test mod", await connection.ExecuteScalarAsync<string>(
            "select name from mod where id = @mod", new { mod }));
    }

    [RequiresPostgresFact]
    public async Task A_token_cannot_create_publish_or_import()
    {
        var (account, secret) = await TokenAsync();
        var mod = await DraftModAsync(account);

        using var client = Bearer(secret);

        using var created = await client.PostAsJsonAsync("/api/v1/mods", new
        {
            id = "Token.Made.This",
            name = "Nope",
            @abstract = "Nope.",
            license = "MIT",
        });

        using var published = await client.PostAsync($"/api/v1/mods/{mod}/publish", null);
        using var imported = await client.PostAsync($"/api/v1/mods/{mod}/releases/import", null);

        Assert.Equal(HttpStatusCode.Unauthorized, created.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, published.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, imported.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task A_token_cannot_mint_or_list_other_tokens()
    {
        // A credential that can make its own successors cannot be revoked: you take one away and
        // it has already made another.
        var (_, secret) = await TokenAsync();

        using var client = Bearer(secret);

        using var listed = await client.GetAsync("/api/v1/me/tokens");
        using var minted = await client.PostAsJsonAsync("/api/v1/me/tokens", new { name = "child" });

        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, minted.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task An_admins_token_reaches_no_admin_surface()
    {
        // Staff hold the most dangerous credentials, so their tokens are the ones that must not
        // inherit the power. Moderation is a session act.
        var (account, secret) = await TokenAsync();

        using var connection = await Db.OpenAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            "update account set site_role = 'admin' where id = @account", new { account });

        using var client = Bearer(secret);
        using var response = await client.GetAsync("/api/v1/admin/overview");

        // 401 rather than 404: the token is a real credential that simply cannot reach this, and
        // the admin surface's own "does not exist" answer is for people, not credentials.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── lifecycle ────────────────────────────────────────────────────────

    [RequiresPostgresFact]
    public async Task A_revoked_token_stops_working_immediately()
    {
        var (account, secret) = await TokenAsync();

        using var connection = await Db.OpenAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            "update api_token set revoked_at = now() where account_id = @account", new { account });

        var tokens = new TokenStore(Db);

        Assert.Null(await tokens.ResolveAsync(secret, default));
    }

    [RequiresPostgresFact]
    public async Task An_expired_token_stops_working_on_its_own()
    {
        var account = await NewAccountAsync();
        var tokens = new TokenStore(Db);

        var issued = await tokens.IssueAsync(
            account, "expired", "personal", DateTimeOffset.UtcNow.AddSeconds(-1), default);

        Assert.Null(await tokens.ResolveAsync(issued.Secret, default));
    }

    [RequiresPostgresFact]
    public async Task Suspending_an_account_kills_its_tokens_too()
    {
        // Otherwise suspension is not suspension: somebody locked out of the browser would keep
        // reading their own drafts through a credential nobody remembered existed.
        var (account, secret) = await TokenAsync();

        using var connection = await Db.OpenAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            "update account set suspended_at = now() where id = @account", new { account });

        var tokens = new TokenStore(Db);

        Assert.Null(await tokens.ResolveAsync(secret, default));
    }

    [RequiresPostgresFact]
    public async Task The_secret_is_never_stored()
    {
        // A leaked database must not hand anybody a usable credential.
        var (account, secret) = await TokenAsync();

        using var connection = await Db.OpenAsync(CancellationToken.None);

        var stored = await connection.QuerySingleAsync<(byte[] Hash, string Prefix)>(
            "select token_hash, prefix from api_token where account_id = @account", new { account });

        Assert.Equal(32, stored.Hash.Length);
        Assert.DoesNotContain(secret, System.Text.Encoding.UTF8.GetString(stored.Hash), StringComparison.Ordinal);

        // What is kept in clear identifies the row and unlocks nothing.
        Assert.StartsWith(stored.Prefix, secret, StringComparison.Ordinal);
        Assert.True(stored.Prefix.Length < secret.Length / 2);
    }

    [RequiresPostgresFact]
    public async Task A_made_up_token_is_simply_nobody()
    {
        var tokens = new TokenStore(Db);

        Assert.Null(await tokens.ResolveAsync("ksm_pat_completelyinvented", default));
        Assert.Null(await tokens.ResolveAsync("not-even-the-right-shape", default));
        Assert.Null(await tokens.ResolveAsync("", default));
    }

    [RequiresPostgresFact]
    public async Task A_token_in_a_query_string_is_ignored()
    {
        // URLs reach proxy logs, browser history and referrer headers. Accepting one there would
        // make the most common accidental leak also the most usable.
        var (account, secret) = await TokenAsync();
        var mod = await DraftModAsync(account);

        using var client = _factory!.CreateClient();
        using var response = await client.GetAsync($"/api/v1/mods/{mod}?access_token={secret}");

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(document.RootElement.GetProperty("your_role").ValueKind is JsonValueKind.Null);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private HttpClient Bearer(string secret)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        return client;
    }

    private async Task<(long Account, string Secret)> TokenAsync(string kind = "personal")
    {
        var account = await NewAccountAsync();
        var issued = await new TokenStore(Db).IssueAsync(account, "test", kind, null, default);

        return (account, issued.Secret);
    }

    private async Task<long> NewAccountAsync()
    {
        using var connection = await Db.OpenAsync(CancellationToken.None);

        var id = await connection.ExecuteScalarAsync<long>(
            "insert into account (handle, display_name) values (@handle, 'Test') returning id",
            new { handle = $"t{Guid.NewGuid():N}"[..16] });

        _accounts.Add(id);
        return id;
    }

    private async Task<string> DraftModAsync(long owner)
    {
        var id = $"test.token.{Guid.NewGuid():N}"[..22];

        using var connection = await Db.OpenAsync(CancellationToken.None);

        await connection.ExecuteAsync("""
            insert into mod (id, id_lower, name, abstract, license, listing_state, created_by)
            values (@id, lower(@id), 'Test mod', 'For tests.', 'MIT', 'unlisted', @owner);

            insert into mod_maintainer (mod_id, account_id, role) values (@id, @owner, 'owner');
            """,
            new { id, owner });

        _mods.Add(id);
        return id;
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Database>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
    }
}
