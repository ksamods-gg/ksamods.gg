using Dapper;
using KsaMods.Api.Data;
using Npgsql;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Setting a validator finding aside.
///
/// <para>Two rules carry the weight. Errors are never suppressible, because an error is what says
/// a release is broken and a moderator who could silence one could make a bad release look clean.
/// And the finding itself is never deleted, so setting one aside stays a visible act with a name
/// and a reason on it rather than a gap nobody can account for.</para>
///
/// <para>The selection is exercised as SQL rather than through the endpoint because that is where
/// the decision actually lives: the handler suppresses exactly the codes this query returns, so a
/// query that let an error through would be a real hole regardless of what the C# around it
/// intended.</para>
/// </summary>
public sealed class FindingSuppressionTests : IAsyncLifetime
{
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
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_database is not null && (_accounts.Count > 0 || _mods.Count > 0))
        {
            using var connection = await _database.OpenAsync(CancellationToken.None);

            // Releases, findings and suppressions all cascade from the mod.
            await connection.ExecuteAsync("delete from mod where id = any(@ids)", new { ids = _mods.ToArray() });
            await connection.ExecuteAsync("delete from account where id = any(@ids)", new { ids = _accounts.ToArray() });
        }

        if (_source is not null) await _source.DisposeAsync();
    }

    /// <summary>A mod with one release carrying an error, a warning and an info finding.</summary>
    private async Task<(string ModId, long ReleaseId, long ActorId)> SeedAsync()
    {
        var accounts = new KsaMods.Api.Auth.AccountStore(Db);
        var actor = await accounts.UpsertAsync(
            "github", $"acct-{Guid.NewGuid():N}", "Test Moderator",
            $"mod{Guid.NewGuid():N}"[..12], null, default);
        _accounts.Add(actor);

        var modId = $"Test{Guid.NewGuid():N}"[..20];
        await new ModRepository(Db).CreateAsync(new ModRow
        {
            Id = modId,
            Type = "mod",
            Name = "A test listing",
            Abstract = "For the suppression tests.",
            License = "MIT",
            Status = "active",
            ListingState = "listed",
            CreatedBy = actor,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }, actor, default);
        _mods.Add(modId);

        using var connection = await Db.OpenAsync(default);

        var releaseId = await connection.ExecuteScalarAsync<long>("""
            insert into mod_release (mod_id, version, version_sort, release_status, released_at,
                                     provider, listing_snapshot, validation_state, availability)
            values (@modId, '1.0.0', '1.0.0', 'stable', now(),
                    'github', '{}'::jsonb, 'passed_warnings', 'verified')
            returning id
            """,
            new { modId });

        await connection.ExecuteAsync("""
            insert into release_finding (release_id, stage, severity, code, message) values
                (@releaseId, 5, 'error',   'broken-thing',  'This one is fatal.'),
                (@releaseId, 5, 'warning', 'odd-path',      'This one is a judgement call.'),
                (@releaseId, 6, 'info',    'worth-knowing', 'This one is a note.')
            """,
            new { releaseId });

        return (modId, releaseId, actor);
    }

    /// <summary>The endpoint's own selection: which codes a suppress request would act on.</summary>
    private async Task<IReadOnlyList<string>> SelectableAsync(long releaseId, string? code)
    {
        using var connection = await Db.OpenAsync(default);

        var rows = await connection.QueryAsync<string>("""
            select code from release_finding
            where release_id = @releaseId
              and severity <> 'error'
              and (@code::text is null or code = @code)
            """,
            new { releaseId, code });

        return rows.ToList();
    }

    [RequiresPostgresFact]
    public async Task An_error_can_never_be_set_aside()
    {
        // The rule the whole feature hangs on. Naming the error code explicitly is the shape an
        // attempt would take, and it has to come back with nothing to act on.
        var (_, releaseId, _) = await SeedAsync();

        Assert.Empty(await SelectableAsync(releaseId, "broken-thing"));
    }

    [RequiresPostgresFact]
    public async Task Setting_aside_everything_still_leaves_the_errors()
    {
        // The blanket action, which is the one most likely to be reached for and the one where a
        // missing severity filter would go unnoticed.
        var (_, releaseId, _) = await SeedAsync();

        var selected = await SelectableAsync(releaseId, null);

        Assert.Equal(2, selected.Count);
        Assert.Contains("odd-path", selected);
        Assert.Contains("worth-knowing", selected);
        Assert.DoesNotContain("broken-thing", selected);
    }

    [RequiresPostgresFact]
    public async Task The_finding_survives_being_set_aside()
    {
        // Suppression is a second row, not an edit or a delete. If it ever became one, the release
        // would quietly lose the record of what the validator actually said.
        var (_, releaseId, actor) = await SeedAsync();

        using var connection = await Db.OpenAsync(default);

        await connection.ExecuteAsync("""
            insert into release_finding_suppression (release_id, code, reason, suppressed_by)
            values (@releaseId, 'odd-path', 'False positive: the loader ships this path.', @actor)
            """,
            new { releaseId, actor });

        var stillThere = await connection.ExecuteScalarAsync<bool>(
            "select exists (select 1 from release_finding where release_id = @releaseId and code = 'odd-path')",
            new { releaseId });

        var reason = await connection.ExecuteScalarAsync<string>(
            "select reason from release_finding_suppression where release_id = @releaseId and code = 'odd-path'",
            new { releaseId });

        Assert.True(stillThere);
        Assert.Equal("False positive: the loader ships this path.", reason);
    }

    [RequiresPostgresFact]
    public async Task A_suppression_needs_a_reason()
    {
        // Enforced in the database as well as at the endpoint. A blank reason six months later is
        // indistinguishable from a mistake, and this table exists for the cases somebody has to
        // justify.
        var (_, releaseId, actor) = await SeedAsync();

        using var connection = await Db.OpenAsync(default);

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("""
            insert into release_finding_suppression (release_id, code, reason, suppressed_by)
            values (@releaseId, 'odd-path', '   ', @actor)
            """,
            new { releaseId, actor }));
    }

    [RequiresPostgresFact]
    public async Task Setting_the_same_finding_aside_twice_replaces_the_reason()
    {
        // The upsert the endpoint relies on. Without it a moderator correcting their own wording
        // would get a primary key violation and no way to fix what they wrote.
        var (_, releaseId, actor) = await SeedAsync();

        using var connection = await Db.OpenAsync(default);

        foreach (var reason in new[] { "First attempt.", "Actually, this is why." })
        {
            await connection.ExecuteAsync("""
                insert into release_finding_suppression (release_id, code, reason, suppressed_by)
                values (@releaseId, 'odd-path', @reason, @actor)
                on conflict (release_id, code)
                  do update set reason = @reason, suppressed_by = @actor, suppressed_at = now()
                """,
                new { releaseId, reason, actor });
        }

        var rows = await connection.QueryAsync<string>(
            "select reason from release_finding_suppression where release_id = @releaseId",
            new { releaseId });

        Assert.Equal(["Actually, this is why."], rows.ToList());
    }
}
