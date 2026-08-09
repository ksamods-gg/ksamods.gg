using Dapper;
using KsaMods.Api.Data;
using Npgsql;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Setting a validator finding aside.
///
/// <para>Two rules carry the weight. The finding itself is never deleted, so setting one aside
/// stays a visible act with a name on it rather than a gap nobody can account for. And clearing
/// an error moves the release's validation state, because an outstanding error is what marks a
/// release failed and a failed release is invisible to everyone but its maintainers and staff.
/// Hiding the finding without moving the state would grey out a line on a page and change nothing
/// anybody cares about.</para>

/// <para>Errors used to be refused outright. What that missed is the case the feature exists for:
/// a loader's archive legitimately has several top-level entries, so the one-top-level-directory
/// check fires on it every time and is simply wrong about that mod. The blanket "all warnings"
/// action still leaves errors alone, so clearing one is always a press somebody aimed at it.</para>
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
              and (@code::text is null or code = @code)
              and (@code::text is not null or severity <> 'error')
            """,
            new { releaseId, code });

        return rows.ToList();
    }

    [RequiresPostgresFact]
    public async Task An_error_can_be_set_aside_when_it_is_named()
    {
        // Errors used to be refused outright. What that missed is the case the feature exists for:
        // a loader's archive legitimately has several top-level entries, so the
        // one-top-level-directory check fires on it every time and is simply wrong about that mod.
        // Refusing to clear a check we got wrong left the release invisible with no way out.
        var (_, releaseId, _) = await SeedAsync();

        Assert.Equal(["broken-thing"], await SelectableAsync(releaseId, "broken-thing"));
    }

    [RequiresPostgresFact]
    public async Task The_blanket_action_still_leaves_the_errors()
    {
        // "Set aside all warnings" says warnings, and sweeping up an error nobody looked at
        // individually is not what anybody pressing it means. Clearing an error stays a press
        // somebody aimed at that error.
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
    public async Task The_reason_column_can_never_be_blank()
    {
        // The reason is optional to a moderator: the endpoint substitutes "No reason given." when
        // they do not write one, because making a one-click judgement into a form stopped people
        // using the feature at all.
        //
        // What is not optional is the column. A row that says a warning was hidden and carries an
        // empty string is indistinguishable from a bug six months later, so the substitution
        // happening at the endpoint is load-bearing and this is the constraint that proves nothing
        // can get past it.
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

    [RequiresPostgresFact]
    public async Task Clearing_the_last_error_un_fails_the_release()
    {
        // The half that matters. A failed release is hidden from everyone but its maintainers and
        // staff, so hiding the finding without moving the state would leave it exactly as
        // invisible as before.
        var (_, releaseId, actor) = await SeedAsync();

        using var connection = await Db.OpenAsync(default);
        await SetStateAsync(connection, releaseId, "failed");

        await SuppressAsync(connection, releaseId, "broken-thing", actor);
        await RecomputeAsync(connection, releaseId);

        Assert.Equal("passed_warnings", await StateAsync(connection, releaseId));
    }

    [RequiresPostgresFact]
    public async Task Putting_the_error_back_fails_the_release_again()
    {
        // Not a one-way door. A moderator who cleared the wrong thing fixes it with the button
        // next to the one they pressed.
        var (_, releaseId, actor) = await SeedAsync();

        using var connection = await Db.OpenAsync(default);
        await SetStateAsync(connection, releaseId, "failed");

        await SuppressAsync(connection, releaseId, "broken-thing", actor);
        await RecomputeAsync(connection, releaseId);
        Assert.Equal("passed_warnings", await StateAsync(connection, releaseId));

        await connection.ExecuteAsync(
            "delete from release_finding_suppression where release_id = @releaseId and code = 'broken-thing'",
            new { releaseId });
        await RecomputeAsync(connection, releaseId);

        Assert.Equal("failed", await StateAsync(connection, releaseId));
    }

    [RequiresPostgresFact]
    public async Task Setting_aside_a_warning_does_not_touch_a_passing_release()
    {
        // A release the validator passed outright is not something this should be moving. Only the
        // failed/passed_warnings pair is ever in play.
        var (_, releaseId, actor) = await SeedAsync();

        using var connection = await Db.OpenAsync(default);
        await SetStateAsync(connection, releaseId, "passed");

        await SuppressAsync(connection, releaseId, "odd-path", actor);
        await RecomputeAsync(connection, releaseId);

        Assert.Equal("passed", await StateAsync(connection, releaseId));
    }

    private static Task SetStateAsync(NpgsqlConnection connection, long releaseId, string state) =>
        connection.ExecuteAsync(
            "update mod_release set validation_state = @state where id = @releaseId",
            new { state, releaseId });

    private static Task SuppressAsync(NpgsqlConnection connection, long releaseId, string code, long actor) =>
        connection.ExecuteAsync("""
            insert into release_finding_suppression (release_id, code, reason, suppressed_by)
            values (@releaseId, @code, 'No reason given.', @actor)
            on conflict (release_id, code) do nothing
            """,
            new { releaseId, code, actor });

    private static Task<string?> StateAsync(NpgsqlConnection connection, long releaseId) =>
        connection.ExecuteScalarAsync<string?>(
            "select validation_state from mod_release where id = @releaseId", new { releaseId });

    /// <summary>
    /// The endpoint's own recompute, kept in step with RecomputeValidationAsync deliberately: the
    /// point of these tests is the state transition, and asserting it against a rule written just
    /// for the test would pass while the real one stayed broken.
    /// </summary>
    private static Task RecomputeAsync(NpgsqlConnection connection, long releaseId) =>
        connection.ExecuteAsync("""
            update mod_release r
               set validation_state = case
                     when r.validation_state = 'failed' and not exists (
                            select 1 from release_finding f
                            where f.release_id = r.id and f.severity = 'error'
                              and not exists (select 1 from release_finding_suppression s
                                               where s.release_id = f.release_id and s.code = f.code))
                       then 'passed_warnings'
                     when r.validation_state = 'passed_warnings' and exists (
                            select 1 from release_finding f
                            where f.release_id = r.id and f.severity = 'error'
                              and not exists (select 1 from release_finding_suppression s
                                               where s.release_id = f.release_id and s.code = f.code))
                       then 'failed'
                     else r.validation_state end
             where r.id = @releaseId
            """,
            new { releaseId });
}
