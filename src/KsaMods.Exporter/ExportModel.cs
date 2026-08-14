using KsaMods.Metadata;

namespace KsaMods.Exporter;

/// <summary>A listing as the exporter sees it. The API projects database rows onto this.</summary>
public sealed record ExportListing
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Authors { get; init; }
    public required string Abstract { get; init; }
    public string? Description { get; init; }
    public required string License { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyDictionary<string, string> Links { get; init; } = new Dictionary<string, string>();
    public string Status { get; init; } = "active";
    public string? SupersededBy { get; init; }
    public IReadOnlyList<string>? Os { get; init; }
    public string? GameMin { get; init; }
    public int? GameMinRevision { get; init; }
    public string? GameMax { get; init; }
    public int? GameMaxRevision { get; init; }
    public ReleasesBlock? Releases { get; init; }
    public InstallBlock? Install { get; init; }
    public ProvidesBlock? Provides { get; init; }
    public LoaderBlock? Loader { get; init; }
    public IReadOnlyList<DependencyEntry> Dependencies { get; init; } = [];

    /// <summary>Excluded from the export: unlisted, delisted and taken-down listings.</summary>
    public string ListingState { get; init; } = "listed";
}

public sealed record ExportRelease
{
    public required string ModId { get; init; }
    public required string Version { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset ReleasedAt { get; init; }
    public required string DownloadUrl { get; init; }
    public required string Sha256 { get; init; }
    public required long Size { get; init; }
    public string ContentType { get; init; } = "application/zip";
    public long? InstallSize { get; init; }
    public string? InstallRoot { get; init; }
    public bool InstallDerived { get; init; }
    public string? GameMin { get; init; }
    public int? GameMinRevision { get; init; }
    public string? GameMax { get; init; }
    public int? GameMaxRevision { get; init; }
    public IReadOnlyList<string>? Os { get; init; }
    public LoaderBlock? Loader { get; init; }
    public IReadOnlyList<DependencyEntry> Dependencies { get; init; } = [];
    public string? ChangelogUrl { get; init; }
    public ListingSnapshot? Listing { get; init; }
    public bool Yanked { get; init; }
    public string? YankedReason { get; init; }
}

public sealed record ExportModlistVersion
{
    public required string ModlistId { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Authors { get; init; }
    public required string Abstract { get; init; }
    public required string License { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public required string Version { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public string? Changelog { get; init; }
    public string? GameMin { get; init; }
    public string? GameMax { get; init; }
    public IReadOnlyList<PinEntry> Mods { get; init; } = [];
    public IReadOnlyList<PinEntry> Vehicles { get; init; } = [];
    public IReadOnlyList<PinEntry> Saves { get; init; } = [];
    public string Visibility { get; init; } = "public";
}

/// <summary>An action recorded in the moderation log, exported so the audit trail leaves the service.</summary>
public sealed record ExportModerationEntry
{
    public required long Id { get; init; }
    public required string Action { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string Rationale { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public long? Supersedes { get; init; }
}

/// <summary>
/// A listing that was published and has since been withdrawn. Its id and its status, and nothing
/// else: it exists so a client can tell "removed" from "never listed" (RFC 0033).
/// </summary>
public sealed record ExportTombstone
{
    public required string Id { get; init; }
    public required string Status { get; init; }
}

public sealed record ExportInput
{
    public IReadOnlyList<ExportListing> Listings { get; init; } = [];
    public IReadOnlyList<ExportTombstone> Tombstones { get; init; } = [];
    public IReadOnlyList<ExportRelease> Releases { get; init; } = [];
    public IReadOnlyList<ExportModlistVersion> Modlists { get; init; } = [];

    /// <summary>Retired modlist ids, exported as a redirect map (backend.md §12.2).</summary>
    public IReadOnlyDictionary<string, string> ModlistAliases { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<ExportModerationEntry> Moderation { get; init; } = [];
    public IReadOnlyList<(int Revision, string VersionString, DateOnly? Released)> Builds { get; init; } = [];

    /// <summary>Passed in rather than read from the clock, so an export run is reproducible.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>A file in the exported tree. Content is already serialised.</summary>
public sealed record ExportFile(string Path, string Content);

public sealed record ExportResult
{
    public required IReadOnlyList<ExportFile> Files { get; init; }

    /// <summary>Listings dropped because they do not satisfy RFC 0031, with the reason.</summary>
    public required IReadOnlyList<(string Id, string Reason)> Skipped { get; init; }
}
