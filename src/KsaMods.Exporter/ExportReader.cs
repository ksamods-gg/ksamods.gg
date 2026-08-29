using System.Text.Json;
using Dapper;
using Npgsql;

namespace KsaMods.Exporter;

/// <summary>
/// Reads the database into the shape <see cref="IndexBuilder"/> serialises.
///
/// <para>Kept separate from the builder so the builder stays a pure function over data - it is
/// where RFC 0031 conformance is decided, and a function that also opens connections is one nobody
/// can test exhaustively.</para>
///
/// <para><b>Only what is public.</b> Unlisted, delisted and taken-down listings are excluded here
/// rather than filtered later: the export is a public artifact that anybody can clone, and a
/// withdrawal that reaches the mirror is a withdrawal that did not happen.</para>
/// </summary>
public static class ExportReader
{
    public static async Task<ExportInput> ReadAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var listings = await ReadListingsAsync(connection);
        var tombstones = await ReadTombstonesAsync(connection);
        var releases = await ReadReleasesAsync(connection);
        var modlists = await ReadModlistsAsync(connection);
        var aliases = await ReadAliasesAsync(connection);
        var moderation = await ReadModerationAsync(connection);
        var builds = await ReadBuildsAsync(connection);

        return new ExportInput
        {
            Listings = listings,
            Tombstones = tombstones,
            Releases = releases,
            Modlists = modlists,
            ModlistAliases = aliases,
            Moderation = moderation,
            Builds = builds,
        };
    }

    private static async Task<IReadOnlyList<ExportListing>> ReadListingsAsync(NpgsqlConnection connection)
    {
        var rows = await connection.QueryAsync<ListingRow>("""
            select m.id, m.type, m.name, m.abstract, m.description, m.license, m.tags,
                   m.links::text as Links, m.status, m.superseded_by as SupersededBy, m.os,
                   m.listing_state as ListingState,
                   m.game_min_display as GameMin, m.game_min_revision as GameMinRevision,
                   m.game_max_display as GameMax, m.game_max_revision as GameMaxRevision,
                   m.install::text as Install, m.provides::text as Provides,
                   -- [releases] is what tells a consumer where new releases will appear, and an
                   -- unverified link is a claim rather than a fact, so only a verified one is
                   -- published. RFC 0033 binds ownership to this host: publishing a repository
                   -- nobody proved control of would hand the ownership check a forged basis.
                   case when l.verified_at is not null then l.provider end as ReleaseProvider,
                   case when l.verified_at is not null then l.repo_full_name end as ReleaseRepo,
                   -- hide_author has to be honoured here too, and this is the easiest place in the
                   -- system to forget it: the static export and the git mirror are public, so a
                   -- name suppressed on the site and published in the index is suppressed nowhere.
                   --
                   -- A placeholder rather than an empty array. RFC 0031 requires at least one
                   -- author, so emitting none would fail the check in IndexBuilder and drop the
                   -- listing out of the export entirely, telling its maintainer their mod was
                   -- rejected for having no author when they are the one who asked for that.
                   case when m.hide_author then array['Anonymous'] else coalesce(
                     (select array_agg(a.display_name order by mm.role, a.handle)
                      from mod_maintainer mm join account a on a.id = mm.account_id
                      where mm.mod_id = m.id), '{}') end as Authors
            from mod m
            left join repo_link l on l.mod_id = m.id
            where m.listing_state = 'listed'
            order by m.id_lower
            """);

        return rows.Select(r => new ExportListing
        {
            Id = r.Id,
            Type = r.Type,
            Name = r.Name,
            Authors = r.Authors,
            Abstract = r.Abstract,
            Description = r.Description,
            License = r.License,
            Tags = r.Tags,
            Links = ReadLinks(r.Links),
            Status = r.Status,
            SupersededBy = r.SupersededBy,
            Os = r.Os,
            GameMin = r.GameMin,
            GameMinRevision = r.GameMinRevision,
            GameMax = r.GameMax,
            GameMaxRevision = r.GameMaxRevision,
            Releases = ReleasesOf(r.ReleaseProvider, r.ReleaseRepo),
            Install = Read<Metadata.InstallBlock>(r.Install),
            Provides = Read<Metadata.ProvidesBlock>(r.Provides),
            ListingState = r.ListingState,
        }).ToList();
    }

    /// <summary>
    /// Withdrawn listings, as ids and states and nothing else.
    ///
    /// <para>A separate query on purpose, rather than widening the one above. RFC 0033 wants a
    /// tombstone in the snapshot so a client can tell "removed" from "never listed" and stop
    /// offering an install it has no other way to learn is gone. But the query above is the one
    /// that reads names, descriptions and links, and its promise is that a withdrawn listing never
    /// enters it - a withdrawal that reaches a public mirror is a withdrawal that did not happen.
    /// Widening it would move that guarantee into whatever the builder does next. This projection
    /// cannot leak a description because it never selects one.</para>
    ///
    /// <para><c>unlisted</c> is deliberately absent: a draft was never published, so "never listed"
    /// is the truth about it and a tombstone would announce something that never existed.</para>
    /// </summary>
    private static async Task<IReadOnlyList<ExportTombstone>> ReadTombstonesAsync(NpgsqlConnection connection)
    {
        var rows = await connection.QueryAsync<(string Id, string State)>("""
            select id as Id, listing_state as State
            from mod
            where listing_state in ('delisted', 'taken_down')
            order by id_lower
            """);

        return rows.Select(r => new ExportTombstone { Id = r.Id, Status = r.State }).ToList();
    }

    /// <summary>
    /// Reads a jsonb column into its record.
    ///
    /// <para>Null on anything malformed rather than throwing: one unreadable blob is not a reason
    /// for the whole export run to fail, and the listing goes out without the section. It cannot
    /// go out with a <i>wrong</i> one - IndexBuilder re-checks the descriptor before publishing,
    /// so a section that survives this is still checked against RFC 0035 on the way out.</para>
    /// </summary>
    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a verified repository link onto RFC 0031's <c>[releases]</c>.
    ///
    /// <para>Only GitHub maps today. The block names hosts a watcher can poll, and there is no key
    /// for GitLab or Codeberg in the format, so a listing released from one of those gets no block
    /// rather than a key nothing reads - which is also the honest answer, since RFC 0033's watcher
    /// could not stamp those releases either.</para>
    /// </summary>
    private static Metadata.ReleasesBlock? ReleasesOf(string? provider, string? repo) =>
        provider == "github" && !string.IsNullOrWhiteSpace(repo)
            ? new Metadata.ReleasesBlock { GitHub = repo }
            : null;

    private static async Task<IReadOnlyList<ExportRelease>> ReadReleasesAsync(NpgsqlConnection connection)
    {
        // Only releases that passed and still resolve. A release whose download is gone stays in
        // the database because modlists pin it, and stays out of the export because the export is
        // a set of promises about things that can be installed.
        var rows = await connection.QueryAsync<ReleaseRow>("""
            select r.id, r.mod_id as ModId, r.version, r.release_status as Status,
                   r.released_at as ReleasedAt, r.install_size as InstallSize,
                   r.install_root as InstallRoot, r.install_derived as InstallDerived,
                   r.game_min_display as GameMin, r.game_min_revision as GameMinRevision,
                   r.game_max_display as GameMax, r.game_max_revision as GameMaxRevision,
                   r.loader_id as LoaderId, r.loader_min as LoaderMin, r.loader_max as LoaderMax,
                   r.changelog_url as ChangelogUrl,
                   r.yanked_at is not null as Yanked, r.yanked_reason as YankedReason,
                   r.listing_snapshot::text as ListingSnapshot,
                   a.url, encode(a.sha256, 'hex') as Sha256, a.size, a.content_type as ContentType
            from mod_release r
            join mod m on m.id = r.mod_id
            join release_artifact a on a.release_id = r.id and not a.is_mirror
            where m.listing_state = 'listed'
              and r.validation_state in ('passed', 'passed_warnings')
              and r.availability in ('verified', 'unverified')
            order by r.mod_id, r.version_sort
            """);

        var dependencies = (await connection.QueryAsync<DependencyRow>("""
            select release_id as ReleaseId, dep_id as DepId, kind, min_version as Min, max_version as Max
            from release_dependency
            where dep_id is not null
            """))
            .GroupBy(d => d.ReleaseId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(r => new ExportRelease
        {
            ModId = r.ModId,
            Version = r.Version,
            Status = r.Status,
            ReleasedAt = r.ReleasedAt,
            DownloadUrl = r.Url,
            Sha256 = r.Sha256,
            Size = r.Size,
            ContentType = r.ContentType,
            InstallSize = r.InstallSize,
            InstallRoot = r.InstallRoot,
            InstallDerived = r.InstallDerived ?? false,
            GameMin = r.GameMin,
            GameMinRevision = r.GameMinRevision,
            GameMax = r.GameMax,
            GameMaxRevision = r.GameMaxRevision,
            Loader = r.LoaderId is null || r.LoaderMin is null
                ? null
                : new Metadata.LoaderBlock { Id = r.LoaderId, Min = r.LoaderMin, Max = r.LoaderMax },
            Dependencies = dependencies.TryGetValue(r.Id, out var deps)
                ? deps.Select(d => new Metadata.DependencyEntry
                {
                    Id = d.DepId,
                    Kind = d.Kind,
                    Min = d.Min,
                    Max = d.Max,
                }).ToList()
                : [],
            ChangelogUrl = r.ChangelogUrl,
            Listing = ReadSnapshot(r.ListingSnapshot),
            Yanked = r.Yanked,
            YankedReason = r.YankedReason,
        }).ToList();
    }

    /// <summary>
    /// The listing as it read at stamp time, recorded on every release since the schema's first
    /// migration and, until now, never read back out. Without it a client browsing an old release
    /// shows today's description of a mod that has changed since, which is the display accuracy
    /// RFC 0031 puts the snapshot there for.
    /// </summary>
    private static Metadata.ListingSnapshot? ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var snapshot = JsonSerializer.Deserialize<Metadata.ListingSnapshot>(json);

            // Name, authors, abstract and license are required on the record, so a snapshot
            // written before one of them existed deserialises into something the format would
            // reject. Dropping it costs display accuracy on one release; emitting it costs
            // conformance on the whole document.
            return snapshot is null || string.IsNullOrWhiteSpace(snapshot.Name) ? null : snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<ExportModlistVersion>> ReadModlistsAsync(NpgsqlConnection connection)
    {
        var rows = await connection.QueryAsync<ModlistRow>("""
            select v.id as VersionId, v.modlist_id as ModlistId, l.name, l.abstract, l.license,
                   l.tags, v.version, v.published_at as PublishedAt, v.changelog,
                   l.visibility,
                   coalesce(
                     (select array_agg(a.display_name order by c.role, a.handle)
                      from modlist_collaborator c join account a on a.id = c.account_id
                      where c.modlist_id = l.id and c.accepted_at is not null), '{}') as Authors
            from modlist_version v
            join modlist l on l.id = v.modlist_id
            where l.visibility = 'public' and l.listing_state = 'listed'
            order by l.id_lower, v.published_at
            """);

        var pins = (await connection.QueryAsync<PinRow>("""
            select p.modlist_version_id as VersionId, p.entry_kind as Kind,
                   p.target_id as TargetId, p.version
            from modlist_pin p
            order by p.position
            """))
            .GroupBy(p => p.VersionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(r =>
        {
            var entries = pins.TryGetValue(r.VersionId, out var found) ? found : [];

            return new ExportModlistVersion
            {
                ModlistId = r.ModlistId,
                Name = r.Name,
                Authors = r.Authors,
                Abstract = r.Abstract,
                License = r.License,
                Tags = r.Tags,
                Version = r.Version,
                PublishedAt = r.PublishedAt,
                Changelog = r.Changelog,
                Mods = Pins(entries, "mod"),
                Vehicles = Pins(entries, "vehicle"),
                Saves = Pins(entries, "save"),
                Visibility = r.Visibility,
            };
        }).ToList();
    }

    private static IReadOnlyList<Metadata.PinEntry> Pins(List<PinRow> entries, string kind) =>
        entries.Where(e => e.Kind == kind)
            .Select(e => new Metadata.PinEntry { Id = e.TargetId, Version = e.Version })
            .ToList();

    private static async Task<IReadOnlyDictionary<string, string>> ReadAliasesAsync(NpgsqlConnection connection)
    {
        var rows = await connection.QueryAsync<(string Alias, string ModlistId)>(
            "select alias_lower::text as Alias, modlist_id from modlist_alias");

        return rows.ToDictionary(r => r.Alias, r => r.ModlistId, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<ExportModerationEntry>> ReadModerationAsync(NpgsqlConnection connection)
    {
        // Public entries only. The log is exported so the audit trail outlives the service, and an
        // entry marked internal was marked that way for a reason that does not stop applying
        // because the data moved.
        var rows = await connection.QueryAsync<ModerationRow>("""
            select id, action, subject_kind as SubjectKind, subject_id as SubjectId,
                   rationale, created_at as CreatedAt, supersedes
            from moderation_action
            where public
            order by id
            """);

        return rows.Select(r => new ExportModerationEntry
        {
            Id = r.Id,
            Action = r.Action,
            SubjectKind = r.SubjectKind,
            SubjectId = r.SubjectId,
            Rationale = r.Rationale,
            CreatedAt = r.CreatedAt,
            Supersedes = r.Supersedes,
        }).ToList();
    }

    private static async Task<IReadOnlyList<(int, string, DateOnly?)>> ReadBuildsAsync(NpgsqlConnection connection)
    {
        var rows = await connection.QueryAsync<BuildRow>(
            "select revision, version_string as VersionString, released_on as ReleasedOn from build order by revision");

        return rows.Select(r => (r.Revision, r.VersionString, r.ReleasedOn)).ToList();
    }

    private static IReadOnlyDictionary<string, string> ReadLinks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // A malformed links blob is not a reason to drop a whole export run. The listing goes
            // out without its links, which is a smaller loss than the index not updating.
            return new Dictionary<string, string>();
        }
    }

    private sealed record ListingRow
    {
        public string Id { get; init; } = "";
        public string Type { get; init; } = "mod";
        public string Name { get; init; } = "";
        public string Abstract { get; init; } = "";
        public string? Description { get; init; }
        public string License { get; init; } = "";
        public string[] Tags { get; init; } = [];
        public string? Links { get; init; }
        public string Status { get; init; } = "active";
        public string? SupersededBy { get; init; }
        public string[]? Os { get; init; }
        public string? GameMin { get; init; }
        public int? GameMinRevision { get; init; }
        public string? GameMax { get; init; }
        public int? GameMaxRevision { get; init; }
        public string? ReleaseProvider { get; init; }
        public string? ReleaseRepo { get; init; }
        public string? Install { get; init; }
        public string? Provides { get; init; }
        public string ListingState { get; init; } = "listed";
        public string[] Authors { get; init; } = [];
    }

    private sealed record ReleaseRow
    {
        public long Id { get; init; }
        public string ModId { get; init; } = "";
        public string Version { get; init; } = "";
        public string Status { get; init; } = "stable";
        public DateTimeOffset ReleasedAt { get; init; }
        public string Url { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public long Size { get; init; }
        public string ContentType { get; init; } = "application/zip";
        public long? InstallSize { get; init; }
        public string? InstallRoot { get; init; }
        public bool? InstallDerived { get; init; }
        public string? GameMin { get; init; }
        public int? GameMinRevision { get; init; }
        public string? GameMax { get; init; }
        public int? GameMaxRevision { get; init; }
        public string? LoaderId { get; init; }
        public string? LoaderMin { get; init; }
        public string? LoaderMax { get; init; }
        public string? ChangelogUrl { get; init; }
        public string? ListingSnapshot { get; init; }
        public bool Yanked { get; init; }
        public string? YankedReason { get; init; }
    }

    private sealed record DependencyRow
    {
        public long ReleaseId { get; init; }
        public string? DepId { get; init; }
        public string Kind { get; init; } = "required";
        public string? Min { get; init; }
        public string? Max { get; init; }
    }

    private sealed record ModlistRow
    {
        public long VersionId { get; init; }
        public string ModlistId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Abstract { get; init; } = "";
        public string License { get; init; } = "";
        public string[] Tags { get; init; } = [];
        public string Version { get; init; } = "";
        public DateTimeOffset PublishedAt { get; init; }
        public string? Changelog { get; init; }
        public string Visibility { get; init; } = "public";
        public string[] Authors { get; init; } = [];
    }

    private sealed record PinRow
    {
        public long VersionId { get; init; }
        public string Kind { get; init; } = "mod";
        public string TargetId { get; init; } = "";
        public string Version { get; init; } = "";
    }

    private sealed record ModerationRow
    {
        public long Id { get; init; }
        public string Action { get; init; } = "";
        public string SubjectKind { get; init; } = "";
        public string SubjectId { get; init; } = "";
        public string Rationale { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
        public long? Supersedes { get; init; }
    }

    private sealed record BuildRow
    {
        public int Revision { get; init; }
        public string VersionString { get; init; } = "";
        public DateOnly? ReleasedOn { get; init; }
    }
}
