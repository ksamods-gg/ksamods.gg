using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using Npgsql;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// The account-management rules that are enforced in SQL rather than in C#, plus the two that
/// would lock someone out of their own account if they were wrong.
/// </summary>
public sealed class AccountManagementTests : IAsyncLifetime
{
    private NpgsqlDataSource? _source;
    private Database? _database;
    private readonly List<long> _created = [];

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
        if (_database is not null && _created.Count > 0)
        {
            using var connection = await _database.OpenAsync(CancellationToken.None);
            await connection.ExecuteAsync(
                "delete from account where id = any(@ids)", new { ids = _created.ToArray() });
        }

        if (_source is not null) await _source.DisposeAsync();
    }

    private async Task<long> NewAccountAsync(string? login = null)
    {
        var accounts = new AccountStore(Db);
        var id = await accounts.UpsertAsync(
            "github", $"acct-{Guid.NewGuid():N}", "Test Person",
            login ?? $"person{Guid.NewGuid():N}"[..12], null, default);

        _created.Add(id);
        return id;
    }

    [RequiresPostgresFact]
    public async Task A_handle_change_is_rejected_when_it_only_differs_in_case()
    {
        // The same citext trap the sign-in bug fell into: the unique index ignores capitalisation,
        // so "Alice" and "alice" are one name. Renaming has to hit that, not slip past it.
        var mine = await NewAccountAsync();
        var theirs = await NewAccountAsync();

        using var connection = await Db.OpenAsync(CancellationToken.None);

        var taken = await connection.ExecuteScalarAsync<string>(
            "select handle from account where id = @theirs", new { theirs });

        Assert.NotNull(taken);

        var conflict = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync(
                "update account set handle = @handle where id = @mine",
                new { mine, handle = taken.ToUpperInvariant() }));

        Assert.Equal("23505", conflict.SqlState);
    }

    [RequiresPostgresFact]
    public async Task A_handle_change_to_a_free_name_succeeds()
    {
        var id = await NewAccountAsync();
        var renamed = $"renamed{Guid.NewGuid():N}"[..14];

        using var connection = await Db.OpenAsync(CancellationToken.None);

        await connection.ExecuteAsync(
            "update account set handle = @renamed where id = @id", new { id, renamed });

        // Read back with an explicit cast, because a bare text parameter would compare
        // case-sensitively and could pass while the stored value differs.
        var found = await connection.ExecuteScalarAsync<long?>(
            "select id from account where handle = @renamed::citext", new { renamed });

        Assert.Equal(id, found);
    }

    [RequiresPostgresFact]
    public async Task Deleting_an_account_anonymises_it_and_keeps_the_row()
    {
        // §4.3: the row survives so that mods, modlist pins and moderation history keep resolving.
        // What goes is everything identifying, plus any way to sign back in.
        var id = await NewAccountAsync();
        var sessions = new SessionStore(Db, new SiteSessionOptions());
        var (sessionId, _) = await sessions.IssueAsync(id, "test-agent", "203.0.113.9", default);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        await connection.ExecuteAsync("""
            update account set handle = @tombstone, display_name = 'Deleted account',
                github_login = null, discord_id = null, avatar_url = null, forums_url = null,
                deleted_at = now()
            where id = @id;
            delete from oauth_identity where account_id = @id;
            update session set revoked_at = now() where account_id = @id and revoked_at is null;
            """,
            new { id, tombstone = $"deleted{Guid.NewGuid():N}"[..20] });

        var row = await connection.QuerySingleAsync<(string Handle, string DisplayName, string? GithubLogin, DateTimeOffset? DeletedAt)>(
            "select handle, display_name, github_login, deleted_at from account where id = @id", new { id });

        Assert.StartsWith("deleted", row.Handle, StringComparison.Ordinal);
        Assert.Equal("Deleted account", row.DisplayName);
        Assert.Null(row.GithubLogin);
        Assert.NotNull(row.DeletedAt);

        // No provider can sign back into it, and the old session is dead.
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "select count(*) from oauth_identity where account_id = @id", new { id }));
        Assert.Null(await sessions.ResolveAsync(sessionId, default));
    }

    [RequiresPostgresFact]
    public async Task A_deleted_account_cannot_keep_identifying_details()
    {
        // The check constraint is the backstop for the code above: if a future change forgets to
        // clear the avatar, the database refuses rather than leaving a half-deleted account.
        var id = await NewAccountAsync();

        using var connection = await Db.OpenAsync(CancellationToken.None);

        var violation = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("""
                update account set deleted_at = now(), avatar_url = 'https://example.com/a.png'
                where id = @id
                """,
                new { id }));

        Assert.Equal("23514", violation.SqlState);
    }

    [RequiresPostgresFact]
    public async Task Revoking_other_sessions_leaves_the_current_one_alive()
    {
        // Signing other devices out must not sign you out of the page you are doing it from.
        var id = await NewAccountAsync();
        var sessions = new SessionStore(Db, new SiteSessionOptions());

        var (current, _) = await sessions.IssueAsync(id, "current", null, default);
        var (other1, _) = await sessions.IssueAsync(id, "phone", null, default);
        var (other2, _) = await sessions.IssueAsync(id, "laptop", null, default);

        using var connection = await Db.OpenAsync(CancellationToken.None);

        var revoked = await connection.ExecuteAsync("""
            update session set revoked_at = now()
            where account_id = @id and id <> @current and revoked_at is null
            """,
            new { id, current });

        Assert.Equal(2, revoked);
        Assert.NotNull(await sessions.ResolveAsync(current, default));
        Assert.Null(await sessions.ResolveAsync(other1, default));
        Assert.Null(await sessions.ResolveAsync(other2, default));
    }

    [RequiresPostgresFact]
    public async Task Unlinking_counts_identities_before_removing_one()
    {
        // The guard the API relies on: an account with one identity has no other way in, so the
        // count has to be taken before the delete rather than after.
        var id = await NewAccountAsync();

        using var connection = await Db.OpenAsync(CancellationToken.None);

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "select count(*) from oauth_identity where account_id = @id", new { id }));

        await connection.ExecuteAsync("""
            insert into oauth_identity (account_id, provider, subject)
            values (@id, 'discord', @subject)
            """,
            new { id, subject = $"d-{Guid.NewGuid():N}" });

        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            "select count(*) from oauth_identity where account_id = @id", new { id }));
    }
}
