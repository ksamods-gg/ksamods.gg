using System.Net;
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
/// The admin surface, over real HTTP against a real database.
///
/// <para>Two things here cannot be checked any other way. The first is the authorisation gate: a
/// unit test of the permission matrix proves the matrix is right, not that every endpoint consults
/// it. The second is the SQL - the admin queries are the most involved in the codebase, and a
/// parameter Postgres cannot type or a column that moved is a runtime failure that compiles
/// perfectly.</para>
/// </summary>
public sealed class AdminEndpointTests : IAsyncLifetime
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

            // The moderation log is append-only by trigger, which is exactly the property one of
            // these tests asserts. Teardown is the one place allowed to reach past it: leaving
            // rows behind would mean the accounts that wrote them could never be removed either,
            // because they are referenced by foreign key.
            await connection.ExecuteAsync("""
                alter table moderation_action disable trigger moderation_action_no_update;
                delete from moderation_action where actor = any(@accounts);
                alter table moderation_action enable trigger moderation_action_no_update;
                """,
                new { accounts = _accounts.ToArray() });

            // Reports go before accounts: resolved_by has no cascade, so a closed report would
            // hold its moderator's row in place.
            await connection.ExecuteAsync(
                "delete from report where subject_id = any(@mods)", new { mods = _mods.ToArray() });

            await connection.ExecuteAsync(
                "delete from mod where id = any(@mods)", new { mods = _mods.ToArray() });

            await connection.ExecuteAsync(
                "delete from account where id = any(@accounts)", new { accounts = _accounts.ToArray() });
        }

        _factory?.Dispose();
        if (_source is not null) await _source.DisposeAsync();
    }

    // ── the gate ──────────────────────────────────────────────────────────

    [RequiresPostgresFact]
    public async Task An_anonymous_caller_is_asked_to_sign_in()
    {
        using var client = Client();
        using var response = await client.GetAsync("/api/v1/admin/overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task An_ordinary_account_is_told_there_is_nothing_there()
    {
        // 404, not 403. A 403 confirms the panel exists and that this person is simply not on the
        // list, which is a fact worth nothing to them and something to an attacker.
        using var client = await SignedInAsync("user");

        foreach (var path in ReadPaths)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [RequiresPostgresFact]
    public async Task A_moderator_can_read_every_queue()
    {
        // The point of looping every path: each one is a distinct query, and this is the only
        // place they are executed against a real Postgres.
        using var client = await SignedInAsync("moderator");

        foreach (var path in ReadPaths)
        {
            using var response = await client.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode,
                $"{path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    [RequiresPostgresFact]
    public async Task Search_and_filter_parameters_are_typed_well_enough_for_Postgres()
    {
        // Every list endpoint takes optional filters that appear in the SQL as `@q is null or …`.
        // An untyped null parameter is a runtime error from the server, never a compile error.
        using var client = await SignedInAsync("moderator");

        string[] paths =
        [
            "/api/v1/admin/reports?state=actioned",
            "/api/v1/admin/listings?q=nothing-matches-this&state=delisted",
            "/api/v1/admin/accounts?q=nobody",
            "/api/v1/admin/log?subjectId=nothing.at.all",
        ];

        foreach (var path in paths)
        {
            using var response = await client.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode,
                $"{path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    // ── withdrawal ────────────────────────────────────────────────────────

    [RequiresPostgresFact]
    public async Task Withdrawing_a_listing_delists_it_and_never_deletes_it()
    {
        var owner = await NewAccountAsync("user");
        var modId = await NewModAsync(owner);

        using var client = await SignedInAsync("moderator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/listings/{modId}/state",
            new { state = "delisted", rationale = "Reported as somebody else's work." });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        // The row survives. Modlists pin releases and dependency graphs name ids, so a hole in the
        // graph breaks installs long after the argument that caused it is settled.
        var state = await connection.ExecuteScalarAsync<string>(
            "select listing_state from mod where id = @modId", new { modId });

        Assert.Equal("delisted", state);
    }

    [RequiresPostgresFact]
    public async Task A_withdrawal_is_written_to_the_log_with_its_reason()
    {
        var owner = await NewAccountAsync("user");
        var modId = await NewModAsync(owner);
        const string why = "Ships a cracked copy of a paid asset pack.";

        using var client = await SignedInAsync("moderator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/listings/{modId}/state", new { state = "taken_down", rationale = why });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        var entry = await connection.QuerySingleAsync<(string Action, string Rationale, bool Public)>("""
            select action, rationale, public from moderation_action
            where subject_kind = 'mod' and subject_id = @modId
            order by id desc limit 1
            """,
            new { modId });

        Assert.Equal("listing_taken_down", entry.Action);
        Assert.Equal(why, entry.Rationale);

        // Public by default: a takedown nobody can see is indistinguishable from content that was
        // never there.
        Assert.True(entry.Public);
    }

    [RequiresPostgresFact]
    public async Task A_withdrawal_without_a_reason_is_refused()
    {
        var owner = await NewAccountAsync("user");
        var modId = await NewModAsync(owner);

        using var client = await SignedInAsync("moderator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/listings/{modId}/state", new { state = "delisted", rationale = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        // Refused entirely, rather than done-but-unexplained.
        Assert.Equal("listed", await connection.ExecuteScalarAsync<string>(
            "select listing_state from mod where id = @modId", new { modId }));
    }

    [RequiresPostgresFact]
    public async Task The_authors_own_vocabulary_cannot_be_written_by_a_moderator()
    {
        // `deprecated` is the author's word about their own work. A moderator writing it would be
        // putting words in their mouth, and a reader would have no way to tell who said it.
        var owner = await NewAccountAsync("user");
        var modId = await NewModAsync(owner);

        using var client = await SignedInAsync("moderator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/listings/{modId}/state",
            new { state = "deprecated", rationale = "Old." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── accounts ──────────────────────────────────────────────────────────

    [RequiresPostgresFact]
    public async Task Suspending_an_account_ends_its_sessions_immediately()
    {
        var (target, targetSession) = await NewAccountWithSessionAsync("user");

        using var client = await SignedInAsync("moderator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{target}/suspend",
            new { suspended = true, rationale = "Posting other people's mods as their own." });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Waiting for the cookie to expire would leave an abusive session live for a month.
        var sessions = new SessionStore(Db, new SiteSessionOptions());
        Assert.Null(await sessions.ResolveAsync(targetSession, default));

        using var connection = await Db.OpenAsync(CancellationToken.None);

        // DateTime, not DateTimeOffset: Npgsql hands a timestamptz back as a UTC DateTime, and
        // Dapper's scalar path converts rather than casts - it cannot reach DateTimeOffset.
        Assert.NotNull(await connection.ExecuteScalarAsync<DateTime?>(
            "select suspended_at from account where id = @target", new { target }));
    }

    [RequiresPostgresFact]
    public async Task You_cannot_suspend_yourself()
    {
        var (id, session) = await NewAccountWithSessionAsync("moderator");

        using var client = Client(session);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{id}/suspend",
            new { suspended = true, rationale = "Testing." });

        // Locking yourself out is never the intent, and undoing it needs somebody else.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task A_moderator_cannot_suspend_an_admin()
    {
        // Otherwise the level below can switch off the level above, and the hierarchy is
        // decoration.
        var admin = await NewAccountAsync("admin");

        using var client = await SignedInAsync("moderator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{admin}/suspend",
            new { suspended = true, rationale = "Disagreement." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);
        Assert.Null(await connection.ExecuteScalarAsync<DateTime?>(
            "select suspended_at from account where id = @admin", new { admin }));
    }

    // ── roles ─────────────────────────────────────────────────────────────

    [RequiresPostgresFact]
    public async Task A_moderator_cannot_hand_out_the_moderator_role()
    {
        // The one action that hands out the power to do everything else. A moderator who could
        // promote could grant themselves an accomplice.
        var target = await NewAccountAsync("user");

        using var client = await SignedInAsync("moderator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{target}/role",
            new { role = "moderator", rationale = "Helping out." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);
        Assert.Equal("user", await connection.ExecuteScalarAsync<string>(
            "select site_role from account where id = @target", new { target }));
    }

    [RequiresPostgresFact]
    public async Task An_admin_can_promote_and_the_change_signs_them_out()
    {
        var (target, targetSession) = await NewAccountWithSessionAsync("user");

        using var client = await SignedInAsync("admin");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{target}/role",
            new { role = "moderator", rationale = "Volunteered, and has been around for years." });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Equal("moderator", await connection.ExecuteScalarAsync<string>(
            "select site_role from account where id = @target", new { target }));

        // A session carries the role it was issued with, so leaving it alive would delay the
        // change until it expired - and in the demotion direction that delay is the whole problem.
        var sessions = new SessionStore(Db, new SiteSessionOptions());
        Assert.Null(await sessions.ResolveAsync(targetSession, default));
    }

    [RequiresPostgresFact]
    public async Task Giving_someone_a_role_needs_no_reason_but_is_still_logged()
    {
        // Nobody is owed a justification for being handed something, and demanding a sentence
        // buys "helping out" rather than information. What is never optional is the record of
        // who did it.
        var target = await NewAccountAsync("user");

        using var client = await SignedInAsync("admin");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{target}/role", new { role = "moderator", rationale = "" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        var handle = await connection.ExecuteScalarAsync<string>(
            "select handle::text from account where id = @target", new { target });

        var entry = await connection.QuerySingleAsync<(string Action, string Rationale)>("""
            select action, rationale from moderation_action
            where subject_kind = 'account' and subject_id = @handle
            order by id desc limit 1
            """,
            new { handle });

        Assert.Equal("role_moderator", entry.Action);
        Assert.False(string.IsNullOrWhiteSpace(entry.Rationale));
    }

    [RequiresPostgresFact]
    public async Task Taking_a_role_back_does_need_a_reason()
    {
        // The other direction is something done *to* somebody. They lose powers they had, and
        // they are owed an explanation in the same log everyone else can read.
        var target = await NewAccountAsync("moderator");

        using var client = await SignedInAsync("admin");

        using var refused = await client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{target}/role", new { role = "user", rationale = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        // Refused entirely, rather than done-but-unexplained.
        Assert.Equal("moderator", await connection.ExecuteScalarAsync<string>(
            "select site_role from account where id = @target", new { target }));

        using var accepted = await client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{target}/role",
            new { role = "user", rationale = "Inactive for a year and asked to be taken off." });

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        Assert.Equal("user", await connection.ExecuteScalarAsync<string>(
            "select site_role from account where id = @target", new { target }));
    }

    [RequiresPostgresFact]
    public async Task An_admin_cannot_change_their_own_role()
    {
        // Demoting yourself could leave the site with no admin at all.
        var (id, session) = await NewAccountWithSessionAsync("admin");

        using var client = Client(session);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{id}/role",
            new { role = "user", rationale = "Stepping back." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── the log ───────────────────────────────────────────────────────────

    [RequiresPostgresFact]
    public async Task The_moderation_log_refuses_to_be_edited()
    {
        var owner = await NewAccountAsync("user");
        var modId = await NewModAsync(owner);

        using var client = await SignedInAsync("moderator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/listings/{modId}/state",
            new { state = "delisted", rationale = "First reason." });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        var id = await connection.ExecuteScalarAsync<long>(
            "select id from moderation_action where subject_id = @modId order by id desc limit 1",
            new { modId });

        // Rewriting a reason after the fact would make the log worse than no log: it would look
        // authoritative while saying whatever the last editor wanted. A correction is a new row.
        var edit = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync(
                "update moderation_action set rationale = 'Something else' where id = @id", new { id }));

        Assert.Equal("P0001", edit.SqlState);

        var remove = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("delete from moderation_action where id = @id", new { id }));

        Assert.Equal("P0001", remove.SqlState);
    }

    [RequiresPostgresFact]
    public async Task The_log_can_be_read_back_for_one_subject()
    {
        var owner = await NewAccountAsync("user");
        var modId = await NewModAsync(owner);

        using var client = await SignedInAsync("moderator");

        using (var write = await client.PostAsJsonAsync(
            $"/api/v1/admin/listings/{modId}/state",
            new { state = "delisted", rationale = "Duplicate of an existing listing." }))
        {
            Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);
        }

        // Case-insensitive, because a mod id is: asking someone to match capitalisation they
        // cannot see would be a puzzle rather than a filter.
        using var response = await client.GetAsync(
            $"/api/v1/admin/log?subjectId={Uri.EscapeDataString(modId.ToUpperInvariant())}");

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entries = document.RootElement.EnumerateArray().ToList();

        Assert.Single(entries);
        Assert.Equal("listing_delisted", entries[0].GetProperty("action").GetString());
        Assert.Equal("Duplicate of an existing listing.", entries[0].GetProperty("rationale").GetString());
    }

    // ── reports ───────────────────────────────────────────────────────────

    [RequiresPostgresFact]
    public async Task Closing_a_report_records_who_closed_it_and_when()
    {
        var owner = await NewAccountAsync("user");
        var modId = await NewModAsync(owner);
        var reportId = await NewReportAsync(modId, owner);

        using var client = await SignedInAsync("moderator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/reports/{reportId}/resolve",
            new { state = "dismissed", rationale = "Works here; the reporter is on an old build." });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        var row = await connection.QuerySingleAsync<(string State, long? ResolvedBy, DateTime? ResolvedAt)>(
            "select state, resolved_by, resolved_at from report where id = @reportId", new { reportId });

        Assert.Equal("dismissed", row.State);
        Assert.NotNull(row.ResolvedBy);
        Assert.NotNull(row.ResolvedAt);
    }

    [RequiresPostgresFact]
    public async Task A_report_cannot_be_closed_without_recording_when()
    {
        // The check constraint is the backstop for the endpoint above: a state change that forgets
        // the timestamp leaves a report that looks handled with no record of the handling.
        var owner = await NewAccountAsync("user");
        var modId = await NewModAsync(owner);
        var reportId = await NewReportAsync(modId, owner);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        var violation = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync(
                "update report set state = 'actioned' where id = @reportId", new { reportId }));

        Assert.Equal("23514", violation.SqlState);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static readonly string[] ReadPaths =
    [
        "/api/v1/admin/overview",
        "/api/v1/admin/reports",
        "/api/v1/admin/reviews",
        "/api/v1/admin/listings",
        "/api/v1/admin/accounts",
        "/api/v1/admin/log",
        "/api/v1/admin/jobs",
    ];

    private HttpClient Client(Guid? session = null)
    {
        var client = (_factory ?? throw new InvalidOperationException("No API host."))
            .CreateClient();

        if (session is { } id)
        {
            client.DefaultRequestHeaders.Add("Cookie", $"ksamods_session={id}");
        }

        return client;
    }

    private async Task<HttpClient> SignedInAsync(string role)
    {
        var (_, session) = await NewAccountWithSessionAsync(role);
        return Client(session);
    }

    private async Task<long> NewAccountAsync(string role)
    {
        using var connection = await Db.OpenAsync(CancellationToken.None);

        var id = await connection.ExecuteScalarAsync<long>("""
            insert into account (handle, display_name, site_role)
            values (@handle, 'Test Person', @role)
            returning id
            """,
            new { handle = $"t{Guid.NewGuid():N}"[..16], role });

        _accounts.Add(id);
        return id;
    }

    private async Task<(long Id, Guid Session)> NewAccountWithSessionAsync(string role)
    {
        var id = await NewAccountAsync(role);
        var sessions = new SessionStore(Db, new SiteSessionOptions());
        var (session, _) = await sessions.IssueAsync(id, "test-agent", null, default);

        return (id, session);
    }

    private async Task<string> NewModAsync(long owner)
    {
        var id = $"test.admin.{Guid.NewGuid():N}"[..24];

        using var connection = await Db.OpenAsync(CancellationToken.None);

        await connection.ExecuteAsync("""
            insert into mod (id, id_lower, name, abstract, license, created_by)
            values (@id, lower(@id), 'Test mod', 'For tests.', 'MIT', @owner);

            insert into mod_maintainer (mod_id, account_id, role)
            values (@id, @owner, 'owner');
            """,
            new { id, owner });

        _mods.Add(id);
        return id;
    }

    private async Task<long> NewReportAsync(string modId, long reporter)
    {
        using var connection = await Db.OpenAsync(CancellationToken.None);

        return await connection.ExecuteScalarAsync<long>("""
            insert into report (subject_kind, subject_id, category, reporter, body)
            values ('mod', @modId, 'broken', @reporter, 'It does not load.')
            returning id
            """,
            new { modId, reporter });
    }

    /// <summary>
    /// Hosts the real API in-process.
    ///
    /// <para>Typed on <see cref="Database"/> rather than <c>Program</c> on purpose: both the API
    /// and the web front end declare a global <c>Program</c>, and a test project referencing both
    /// cannot name either. Any public type from the API assembly locates the same entry point.</para>
    /// </summary>
    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Database>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
    }
}
