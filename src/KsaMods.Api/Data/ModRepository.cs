using Dapper;
using KsaMods.Api.Domain;

namespace KsaMods.Api.Data;

public sealed record ModRow
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required string Abstract { get; init; }
    public string? Description { get; init; }
    public required string License { get; init; }
    public string[] Tags { get; init; } = [];
    public string Links { get; init; } = "{}";
    public required string Status { get; init; }
    public string? SupersededBy { get; init; }
    public required string ListingState { get; init; }
    public string[]? Os { get; init; }
    public required long CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ReleaseRow
{
    public required long Id { get; init; }
    public required string ModId { get; init; }
    public required string Version { get; init; }
    public required string ReleaseStatus { get; init; }
    public required DateTimeOffset ReleasedAt { get; init; }
    public string? ProviderTag { get; init; }
    public string? ProviderCommit { get; init; }
    public string? ChangelogUrl { get; init; }
    public int? GameMinRevision { get; init; }
    public int? GameMaxRevision { get; init; }
    public string? GameMinDisplay { get; init; }
    public string? GameMaxDisplay { get; init; }
    public string? LoaderId { get; init; }
    public string? LoaderMin { get; init; }
    public string? LoaderMax { get; init; }
    public string? InstallRoot { get; init; }
    public long? InstallSize { get; init; }
    public required string ValidationState { get; init; }
    public required string Availability { get; init; }
    public DateTimeOffset? LastVerifiedAt { get; init; }
    public DateTimeOffset? YankedAt { get; init; }
    public string? YankedReason { get; init; }
}

public sealed class ModRepository(Database database)
{
    public async Task<ModRow?> FindAsync(string id, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return await connection.QuerySingleOrDefaultAsync<ModRow>("""
            select id as Id, type as Type, name as Name, abstract as "Abstract",
                   description as Description, license as License, tags as Tags,
                   links::text as Links, status as Status, superseded_by as SupersededBy,
                   listing_state as ListingState, os as Os, created_by as CreatedBy,
                   created_at as CreatedAt, updated_at as UpdatedAt
            from mod
            where id_lower = @id
            """,
            new { id = id.ToLowerInvariant() });
    }

    /// <summary>
    /// Reserves an id across <b>both</b> mods and modlists in one statement.
    ///
    /// <para>The namespace is global (RFC 0031) and the uniqueness check is case-insensitive, so
    /// this has to be one query rather than two lookups with a race between them.</para>
    /// </summary>
    public async Task<bool> IsIdAvailableAsync(string id, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var taken = await connection.ExecuteScalarAsync<bool>("""
            select exists (select 1 from mod where id_lower = @id)
                or exists (select 1 from modlist where id_lower = @id)
                or exists (select 1 from modlist_alias where alias_lower = @id)
            """,
            new { id = id.ToLowerInvariant() });

        return !taken;
    }

    public async Task CreateAsync(ModRow mod, long ownerAccountId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync("""
            insert into mod (id, id_lower, type, name, abstract, description, license, tags,
                             links, status, listing_state, os, created_by)
            values (@Id, lower(@Id), @Type, @Name, @Abstract, @Description, @License, @Tags,
                    @Links::jsonb, @Status, @ListingState, @Os, @CreatedBy)
            """,
            mod, transaction);

        await connection.ExecuteAsync("""
            insert into mod_maintainer (mod_id, account_id, role)
            values (@modId, @accountId, 'owner')
            """,
            new { modId = mod.Id, accountId = ownerAccountId }, transaction);

        transaction.Commit();
    }

    /// <summary>The caller's role on this mod, or null. Feeds <see cref="Permissions"/>.</summary>
    public async Task<string?> RoleOfAsync(string modId, long accountId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return await connection.ExecuteScalarAsync<string?>("""
            select role from mod_maintainer
            where mod_id = (select id from mod where id_lower = @modId) and account_id = @accountId
            """,
            new { modId = modId.ToLowerInvariant(), accountId });
    }

    public async Task<IReadOnlyList<ReleaseRow>> ReleasesAsync(string modId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var rows = await connection.QueryAsync<ReleaseRow>("""
            select r.id as Id, r.mod_id as ModId, r.version as Version,
                   r.release_status as ReleaseStatus, r.released_at as ReleasedAt,
                   r.provider_tag as ProviderTag, r.provider_commit as ProviderCommit,
                   r.changelog_url as ChangelogUrl,
                   r.game_min_revision as GameMinRevision, r.game_max_revision as GameMaxRevision,
                   r.game_min_display as GameMinDisplay, r.game_max_display as GameMaxDisplay,
                   r.loader_id as LoaderId, r.loader_min as LoaderMin, r.loader_max as LoaderMax,
                   r.install_root as InstallRoot, r.install_size as InstallSize,
                   r.validation_state as ValidationState, r.availability as Availability,
                   r.last_verified_at as LastVerifiedAt,
                   r.yanked_at as YankedAt, r.yanked_reason as YankedReason
            from mod_release r
            join mod m on m.id = r.mod_id
            where m.id_lower = @modId
            order by r.version_sort desc
            """,
            new { modId = modId.ToLowerInvariant() });

        return [.. rows];
    }

    /// <summary>
    /// Every release declaring an asset id — the collision query.
    ///
    /// <para>KSA registers ids with <c>TryAdd</c> into one global table, so a duplicate from a
    /// later mod is silently discarded with no error a user will ever find. One indexed lookup
    /// is what turns that into something the site can show.</para>
    /// </summary>
    public async Task<IReadOnlyList<(string ModId, string Version, string XmlPath)>> CollisionsAsync(
        string assetId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var rows = await connection.QueryAsync<(string, string, string)>("""
            select r.mod_id, r.version, a.xml_path
            from release_asset_id a
            join mod_release r on r.id = a.release_id
            join mod m on m.id = r.mod_id
            where a.asset_id = @assetId
              and m.listing_state = 'listed'
              and r.yanked_at is null
            order by r.mod_id, r.version_sort desc
            """,
            new { assetId });

        return [.. rows];
    }

    public async Task<IReadOnlyList<Finding>> FindingsAsync(long releaseId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var rows = await connection.QueryAsync<(short Stage, string Severity, string Code, string Message, string? Path)>("""
            select stage, severity, code, message, path
            from release_finding
            where release_id = @releaseId
            order by
                case severity when 'error' then 0 when 'warning' then 1 else 2 end,
                stage, code
            """,
            new { releaseId });

        return
        [
            .. rows.Select(r => new Finding(
                r.Stage,
                Enum.Parse<KsaMods.Metadata.Severity>(r.Severity, ignoreCase: true),
                r.Code, r.Message, r.Path)),
        ];
    }
}

public sealed record Finding(int Stage, KsaMods.Metadata.Severity Severity, string Code, string Message, string? Path);
