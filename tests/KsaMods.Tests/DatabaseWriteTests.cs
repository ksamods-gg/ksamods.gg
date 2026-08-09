using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using Npgsql;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Exercises the transactional write paths against a real Postgres.
///
/// <para>These exist because of a bug that shipped: every repository that began a transaction
/// called <c>connection.Open()</c> on a connection the factory had already opened, and Npgsql
/// throws rather than ignoring it. Nothing caught it - the read paths never opened explicitly, so
/// the whole suite passed while sign-in was broken in production. Pure unit tests could not have
/// caught it either, which is the argument for this file existing at all.</para>
///
/// <para>Skipped when <c>KSAMODS_TEST_POSTGRES</c> is unset, so the ordinary suite runs offline.</para>
/// </summary>
public sealed class DatabaseWriteTests : IAsyncLifetime
{
    private NpgsqlDataSource? _source;
    private Database? _database;

    private Database Db => _database ?? throw new InvalidOperationException("No test database.");

    public Task InitializeAsync()
    {
        if (RequiresPostgresFactAttribute.Available)
        {
            _source = Database.CreateDataSource(RequiresPostgresFactAttribute.ConnectionString!);
            _database = new Database(_source);
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_source is not null) await _source.DisposeAsync();
    }

    [RequiresPostgresFact]
    public async Task Opening_a_connection_returns_one_that_is_already_open()
    {
        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);

        // The exact call that shipped broken. Asserting the throw documents why no repository may
        // do this, rather than leaving the next person to rediscover it in production.
        Assert.Throws<InvalidOperationException>(connection.Open);
    }

    [RequiresPostgresFact]
    public async Task Beginning_a_transaction_yields_a_usable_connection()
    {
        var (connection, transaction) = await Db.BeginTransactionAsync(CancellationToken.None);

        try
        {
            var one = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("select 1", transaction: transaction));

            Assert.Equal(1, one);
            await transaction.RollbackAsync();
        }
        finally
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    [RequiresPostgresFact]
    public async Task Signing_in_twice_reuses_the_same_account()
    {
        // The path that threw "Connection already open" on the first real sign-in.
        var accounts = new AccountStore(Db);
        var subject = $"test-{Guid.NewGuid():N}";

        var first = await accounts.UpsertAsync("github", subject, "Test User", "testuser", null, default);
        var second = await accounts.UpsertAsync("github", subject, "Test User", "testuser", null, default);

        Assert.Equal(first, second);

        await DeleteAccountsAsync(first);
    }

    [RequiresPostgresFact]
    public async Task A_handle_collision_does_not_fail_the_sign_in()
    {
        // Two people whose provider logins sanitise to the same handle. The second must still get
        // an account: failing a sign-in over a name clash the user did not cause is a dead end
        // they have no way out of.
        var accounts = new AccountStore(Db);
        var login = $"dup{Guid.NewGuid():N}"[..12];

        var first = await accounts.UpsertAsync("github", $"s1-{Guid.NewGuid():N}", "A", login, null, default);
        var second = await accounts.UpsertAsync("discord", $"s2-{Guid.NewGuid():N}", "B", login, null, default);

        Assert.NotEqual(first, second);

        using (var connection = await Db.OpenAsync(CancellationToken.None))
        {
            var handles = await connection.QueryAsync<string>(
                "select handle from account where id = any(@ids)", new { ids = new[] { first, second } });

            Assert.Equal(2, handles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        await DeleteAccountsAsync(first, second);
    }

    [RequiresPostgresFact]
    public async Task A_handle_differing_only_in_case_does_not_break_sign_in()
    {
        // The 23505 that reached production. account.handle is citext, so the unique index
        // compares case-insensitively - but a `where handle = @param` lookup does not, because
        // Postgres casts the column down to text to find an operator. Anyone whose login differed
        // only in case from an existing handle got a duplicate-key crash instead of an account.
        var accounts = new AccountStore(Db);
        var stem = $"Case{Guid.NewGuid():N}"[..12];

        var first = await accounts.UpsertAsync("github", $"c1-{Guid.NewGuid():N}", "A", stem, null, default);
        var second = await accounts.UpsertAsync(
            "discord", $"c2-{Guid.NewGuid():N}", "B", stem.ToUpperInvariant(), null, default);

        Assert.NotEqual(first, second);

        using (var connection = await Db.OpenAsync(CancellationToken.None))
        {
            var handles = (await connection.QueryAsync<string>(
                "select handle from account where id = any(@ids)",
                new { ids = new[] { first, second } })).ToList();

            // Distinct even ignoring case, which is the only kind of distinct the index accepts.
            Assert.Equal(2, handles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        await DeleteAccountsAsync(first, second);
    }

    [RequiresPostgresFact]
    public async Task Concurrent_first_time_sign_ins_settle_on_one_account()
    {
        // Check-then-act on the handle meant two simultaneous sign-ins could both see a name free
        // and both try to take it. Same subject twice must converge on one account, not throw.
        var accounts = new AccountStore(Db);
        var subject = $"race-{Guid.NewGuid():N}";

        var results = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ =>
                accounts.UpsertAsync("github", subject, "Racer", $"racer{Guid.NewGuid():N}"[..10], null, default)));

        Assert.Single(results.Distinct());

        await DeleteAccountsAsync(results.Distinct().ToArray());
    }

    [RequiresPostgresFact]
    public async Task Concurrent_sign_ins_sharing_a_login_stem_all_succeed()
    {
        // Different people, same derived handle, at the same moment. Every one must end up with
        // an account: losing a sign-in to someone else's name clash is not an acceptable outcome.
        var accounts = new AccountStore(Db);
        var stem = $"shared{Guid.NewGuid():N}"[..10];

        var results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(i =>
                accounts.UpsertAsync("github", $"s{i}-{Guid.NewGuid():N}", "Person", stem, null, default)));

        Assert.Equal(5, results.Distinct().Count());

        await DeleteAccountsAsync(results);
    }

    [RequiresPostgresFact]
    public async Task Issuing_and_revoking_a_session_round_trips()
    {
        var accounts = new AccountStore(Db);
        var sessions = new SessionStore(Db, new SiteSessionOptions());

        var accountId = await accounts.UpsertAsync(
            "github", $"sess-{Guid.NewGuid():N}", "Session User", "sessionuser", null, default);

        var (sessionId, _) = await sessions.IssueAsync(accountId, "test-agent", "203.0.113.7", default);

        var resolved = await sessions.ResolveAsync(sessionId, default);
        Assert.NotNull(resolved);
        Assert.Equal(accountId, resolved.AccountId);

        // Revocation has to take effect immediately - that is the whole reason sessions are rows
        // rather than self-contained tokens.
        await sessions.RevokeAsync(sessionId, default);
        Assert.Null(await sessions.ResolveAsync(sessionId, default));

        await DeleteAccountsAsync(accountId);
    }

    [RequiresPostgresFact]
    public async Task Creating_a_mod_commits_the_listing_and_its_owner_together()
    {
        var accounts = new AccountStore(Db);
        var mods = new ModRepository(Db);

        var accountId = await accounts.UpsertAsync(
            "github", $"mod-{Guid.NewGuid():N}", "Mod Owner", "modowner", null, default);

        var id = $"TestMod{Guid.NewGuid():N}"[..20];

        await mods.CreateAsync(new ModRow
        {
            Id = id,
            Type = "mod",
            Name = "Test",
            Abstract = "A test listing.",
            License = "MIT",
            Links = "{}",
            Status = "active",
            ListingState = "listed",
            CreatedBy = accountId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }, accountId, default);

        Assert.Equal("owner", await mods.RoleOfAsync(id, accountId, default));

        using (var connection = await Db.OpenAsync(CancellationToken.None))
        {
            await connection.ExecuteAsync("delete from mod where id_lower = @id", new { id = id.ToLowerInvariant() });
        }

        await DeleteAccountsAsync(accountId);
    }

    private async Task DeleteAccountsAsync(params long[] ids)
    {
        using var connection = await Db.OpenAsync(CancellationToken.None);
        await connection.ExecuteAsync("delete from account where id = any(@ids)", new { ids });
    }
}
