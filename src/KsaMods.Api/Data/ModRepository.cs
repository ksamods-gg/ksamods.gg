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
    public string? BannerUrl { get; init; }
    public string? IconUrl { get; init; }
    public string[]? Os { get; init; }
    public required long CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Somebody with a role on a listing, as the manage screen shows them.</summary>
public sealed record MaintainerRow
{
    public required string Handle { get; init; }
    public required string DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
    public required string Role { get; init; }
    public required DateTimeOffset AddedAt { get; init; }
}

/// <summary>Just enough of an account to add it to something.</summary>
public sealed record AccountRef
{
    public required long Id { get; init; }
    public required string Handle { get; init; }
    public DateTimeOffset? SuspendedAt { get; init; }
}

/// <summary>What currently points at a mod. All zero means nothing breaks if it goes away.</summary>
public sealed record ModReferences
{
    public int Releases { get; init; }
    public int PublishedPins { get; init; }
    public int DraftEntries { get; init; }
    public int Dependents { get; init; }
    public int Successors { get; init; }

    public bool IsUnused =>
        Releases == 0 && PublishedPins == 0 && DraftEntries == 0 && Dependents == 0 && Successors == 0;

    /// <summary>Why deletion was refused, in words an author can act on.</summary>
    public string Explain() => this switch
    {
        { Releases: > 0 } => "It has published releases. Unlist it instead, so anything pinning it keeps working.",
        { PublishedPins: > 0 } => "A published modlist pins this mod. Unlist it instead.",
        { DraftEntries: > 0 } => "Someone has it in a modlist draft. Unlist it instead.",
        { Dependents: > 0 } => "Another mod's release depends on it. Unlist it instead.",
        { Successors: > 0 } => "Another listing names this one as its successor. Unlist it instead.",
        _ => "It is still referenced.",
    };
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
                   listing_state as ListingState, banner_url as BannerUrl,
                   icon_url as IconUrl,
                   os as Os, created_by as CreatedBy,
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
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync("""
            insert into mod (id, id_lower, type, name, abstract, description, license, tags,
                             links, status, listing_state, banner_url, icon_url, os, created_by)
            values (@Id, lower(@Id), @Type, @Name, @Abstract, @Description, @License, @Tags,
                    @Links::jsonb, @Status, @ListingState, @BannerUrl, @IconUrl, @Os, @CreatedBy)
            """,
            mod, transaction);

        await connection.ExecuteAsync("""
            insert into mod_maintainer (mod_id, account_id, role)
            values (@modId, @accountId, 'owner')
            """,
            new { modId = mod.Id, accountId = ownerAccountId }, transaction);

        transaction.Commit();
    }

    /// <summary>
    /// Updates the parts of a listing an author is allowed to change after creation.
    ///
    /// <para>The id is not among them and never will be: it is the folder name the game loads
    /// the mod under, so renaming it breaks every install and every modlist that pinned it.</para>
    /// </summary>
    public async Task UpdateAsync(ModRow mod, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        await connection.ExecuteAsync("""
            update mod
               set name        = @Name,
                   abstract    = @Abstract,
                   description = @Description,
                   license     = @License,
                   tags        = @Tags,
                   links       = @Links::jsonb,
                   banner_url  = @BannerUrl,
                   icon_url    = @IconUrl,
                   updated_at  = now()
             where id_lower = lower(@Id)
            """,
            mod);
    }

    /// <summary>
    /// Moves a listing between 'listed' and 'unlisted'.
    ///
    /// <para>Deliberately cannot reach 'delisted' or 'taken_down': those are moderation states,
    /// and an author who could set them could also clear one, which would undo a takedown.</para>
    /// </summary>
    public async Task<bool> SetListingStateAsync(string id, string state, CancellationToken ct)
    {
        if (state is not ("listed" or "unlisted")) return false;

        using var connection = await database.OpenAsync(ct);

        var rows = await connection.ExecuteAsync("""
            update mod set listing_state = @state, updated_at = now()
             where id_lower = lower(@id) and listing_state in ('listed', 'unlisted')
            """,
            new { id, state });

        return rows > 0;
    }

    /// <summary>
    /// Everything that would be left dangling if this listing vanished.
    ///
    /// <para>Deleting is only offered while all of these are zero. The site promises that ids stay
    /// resolvable so other people's modlists do not break, and that promise is worth more than
    /// the convenience of removing a listing somebody else already depends on. A listing nobody
    /// has touched yet, which is what a mistaken 'test' entry is, has nothing pointing at it and
    /// can go.</para>
    /// </summary>
    public async Task<ModReferences> ReferencesAsync(string id, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return await connection.QuerySingleAsync<ModReferences>("""
            -- count(*) is bigint in Postgres and these are int, and Dapper will not narrow that
            -- for you: without the casts this throws at materialisation rather than at compile
            -- time, which is a 500 on a path whose whole job is to explain a refusal politely.
            select
              (select count(*) from mod_release        where mod_id = m.id)::int                      as Releases,
              (select count(*) from modlist_pin        where entry_kind = 'mod' and target_id = m.id)::int as PublishedPins,
              (select count(*) from modlist_draft_entry where entry_kind = 'mod' and target_id = m.id)::int as DraftEntries,
              (select count(*) from release_dependency where dep_id = m.id)::int                      as Dependents,
              (select count(*) from mod                where superseded_by = m.id)::int               as Successors
            from mod m
            where m.id_lower = lower(@id)
            """,
            new { id });
    }

    /// <summary>
    /// Removes a listing outright. Callers must have checked <see cref="ReferencesAsync"/> first.
    ///
    /// <para>Maintainers and the repository link cascade with it. Nothing else should exist, by
    /// definition of the check that has to precede this.</para>
    /// </summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var rows = await connection.ExecuteAsync(
            "delete from mod where id_lower = lower(@id)", new { id });

        return rows > 0;
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

    /// <summary>Everyone with a role on this listing, owner first.</summary>
    public async Task<IReadOnlyList<MaintainerRow>> MaintainersAsync(string modId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var rows = await connection.QueryAsync<MaintainerRow>("""
            select a.handle as Handle, a.display_name as DisplayName, a.avatar_url as AvatarUrl,
                   m.role as Role, m.added_at as AddedAt
            from mod_maintainer m
            join account a on a.id = m.account_id
            where m.mod_id = (select id from mod where id_lower = @modId)
            order by case when m.role = 'owner' then 0 else 1 end, a.handle
            """,
            new { modId = modId.ToLowerInvariant() });

        return rows.ToList();
    }

    /// <summary>An account by handle, or null. Handles are citext, so the comparison is already case-insensitive.</summary>
    public async Task<AccountRef?> FindAccountAsync(string handle, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        return await connection.QuerySingleOrDefaultAsync<AccountRef?>("""
            select id as Id, handle as Handle, suspended_at as SuspendedAt
            from account where handle = @handle
            """,
            new { handle });
    }

    /// <summary>
    /// Adds a collaborator. Always as a maintainer, never as an owner: there is exactly one owner
    /// per listing and it is changed by transferring, not by adding a second one.
    /// </summary>
    public async Task<bool> AddMaintainerAsync(string modId, long accountId, long addedBy, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var rows = await connection.ExecuteAsync("""
            insert into mod_maintainer (mod_id, account_id, role, added_by)
            select id, @accountId, 'maintainer', @addedBy from mod where id_lower = @modId
            on conflict (mod_id, account_id) do nothing
            """,
            new { modId = modId.ToLowerInvariant(), accountId, addedBy });

        return rows > 0;
    }

    /// <summary>Removes a collaborator. Refuses to remove the owner, which would leave the listing ownerless.</summary>
    public async Task<bool> RemoveMaintainerAsync(string modId, long accountId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var rows = await connection.ExecuteAsync("""
            delete from mod_maintainer
            where mod_id = (select id from mod where id_lower = @modId)
              and account_id = @accountId
              and role <> 'owner'
            """,
            new { modId = modId.ToLowerInvariant(), accountId });

        return rows > 0;
    }

    /// <summary>
    /// Hands a listing to somebody else.
    ///
    /// <para>One transaction, and the demotion happens before the promotion: there is a unique
    /// index allowing a single owner per listing, so promoting first would collide with the owner
    /// still sitting there. The previous owner stays on as a maintainer rather than being dropped,
    /// because handing over a project is not the same as leaving it.</para>
    /// </summary>
    public async Task TransferOwnershipAsync(string modId, long newOwnerAccountId, long actingAccountId, CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();

        var id = await connection.ExecuteScalarAsync<string>(
            "select id from mod where id_lower = @modId",
            new { modId = modId.ToLowerInvariant() }, transaction);

        await connection.ExecuteAsync(
            "update mod_maintainer set role = 'maintainer' where mod_id = @id and role = 'owner'",
            new { id }, transaction);

        // The new owner may already be a maintainer, so this is an upsert rather than an insert.
        await connection.ExecuteAsync("""
            insert into mod_maintainer (mod_id, account_id, role, added_by)
            values (@id, @accountId, 'owner', @addedBy)
            on conflict (mod_id, account_id) do update set role = 'owner'
            """,
            new { id, accountId = newOwnerAccountId, addedBy = actingAccountId }, transaction);

        transaction.Commit();
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
    /// Every release declaring an asset id - the collision query.
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
