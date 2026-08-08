using Dapper;
using KsaMods.Api.Domain;

namespace KsaMods.Api.Data;

public sealed record ModlistRow
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Abstract { get; init; }
    public string? Description { get; init; }
    public required string License { get; init; }
    public string[] Tags { get; init; } = [];
    public required string Visibility { get; init; }
    public required string ListingState { get; init; }
    public required int DraftRevision { get; init; }
    public required long CreatedBy { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed class DraftConflictException(int expected, int actual)
    : Exception($"Draft revision {expected} is stale; the current revision is {actual}.")
{
    public int Expected { get; } = expected;
    public int Actual { get; } = actual;
}

public sealed class ModlistRepository(Database database)
{
    public async Task<ModlistRow?> FindAsync(string id, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return await connection.QuerySingleOrDefaultAsync<ModlistRow>("""
            select id as Id, name as Name, abstract as "Abstract", description as Description,
                   license as License, tags as Tags, visibility as Visibility,
                   listing_state as ListingState, draft_revision as DraftRevision,
                   created_by as CreatedBy, updated_at as UpdatedAt
            from modlist
            where id_lower = @id
            """,
            new { id = id.ToLowerInvariant() });
    }

    /// <summary>
    /// Resolves a possibly-retired id. A mod disputing an id held by a modlist wins by rule
    /// (§2.1), and the alias is what keeps the old links and exports resolving afterwards.
    /// </summary>
    public async Task<string?> ResolveAliasAsync(string id, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return await connection.ExecuteScalarAsync<string?>(
            "select modlist_id from modlist_alias where alias_lower = @id",
            new { id = id.ToLowerInvariant() });
    }

    public async Task<string?> RoleOfAsync(string modlistId, long accountId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return await connection.ExecuteScalarAsync<string?>("""
            select role from modlist_collaborator
            where modlist_id = (select id from modlist where id_lower = @modlistId)
              and account_id = @accountId
              and accepted_at is not null
            """,
            new { modlistId = modlistId.ToLowerInvariant(), accountId });
    }

    public async Task<IReadOnlyList<DraftEntry>> DraftAsync(string modlistId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var rows = await connection.QueryAsync<DraftEntry>("""
            select entry_kind as Kind, target_id as TargetId, pinned_version as PinnedVersion,
                   position as Position, note as Note
            from modlist_draft_entry
            where modlist_id = (select id from modlist where id_lower = @modlistId)
            order by position
            """,
            new { modlistId = modlistId.ToLowerInvariant() });

        return [.. rows];
    }

    /// <summary>
    /// Applies a draft mutation under optimistic concurrency.
    ///
    /// <para>Two people editing one draft is the normal case, not the edge case (§6.3). The
    /// revision check and the mutation share a transaction, so a stale write loses cleanly with a
    /// 409 carrying the current state rather than silently clobbering the other editor.</para>
    /// </summary>
    public async Task<int> MutateDraftAsync(
        string modlistId,
        int expectedRevision,
        Func<System.Data.IDbConnection, System.Data.IDbTransaction, string, Task> mutate,
        CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var current = await connection.QuerySingleOrDefaultAsync<(string Id, int Revision)?>("""
            select id, draft_revision from modlist where id_lower = @modlistId for update
            """,
            new { modlistId = modlistId.ToLowerInvariant() }, transaction);

        if (current is null) throw new KeyNotFoundException(modlistId);

        if (current.Value.Revision != expectedRevision)
        {
            throw new DraftConflictException(expectedRevision, current.Value.Revision);
        }

        await mutate(connection, transaction, current.Value.Id);

        var next = await connection.ExecuteScalarAsync<int>("""
            update modlist
            set draft_revision = draft_revision + 1, updated_at = now()
            where id = @id
            returning draft_revision
            """,
            new { id = current.Value.Id }, transaction);

        transaction.Commit();
        return next;
    }

    /// <summary>
    /// Snapshots the draft into an immutable version. The draft survives and stays editable.
    /// </summary>
    public async Task<long> PublishAsync(
        string modlistId,
        string version,
        byte[] versionSort,
        string? changelog,
        PublishResult prepared,
        long publishedBy,
        CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var id = await connection.ExecuteScalarAsync<long>("""
            insert into modlist_version
                (modlist_id, version, version_sort, changelog,
                 game_min_revision, game_max_revision, published_by)
            values
                ((select id from modlist where id_lower = @modlistId), @version, @versionSort, @changelog,
                 @gameMin, @gameMax, @publishedBy)
            returning id
            """,
            new
            {
                modlistId = modlistId.ToLowerInvariant(),
                version,
                versionSort,
                changelog,
                gameMin = prepared.GameMinRevision,
                gameMax = prepared.GameMaxRevision,
                publishedBy,
            },
            transaction);

        var position = 0;
        foreach (var (kind, pins) in new[]
                 {
                     ("mod", prepared.Mods),
                     ("vehicle", prepared.Vehicles),
                     ("save", prepared.Saves),
                 })
        {
            foreach (var pin in pins)
            {
                await connection.ExecuteAsync("""
                    insert into modlist_pin (modlist_version_id, entry_kind, target_id, version, position)
                    values (@id, @kind, @targetId, @version, @position)
                    """,
                    new { id, kind, targetId = pin.Id, version = pin.Version, position = position++ },
                    transaction);
            }
        }

        transaction.Commit();
        return id;
    }
}
