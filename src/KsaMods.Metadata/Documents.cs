using System.Text.Json.Serialization;

namespace KsaMods.Metadata;

/// <summary>Content types. New types arrive by RFC and extend this without a spec bump.</summary>
public static class ContentType
{
    public const string Mod = "mod";
    public const string ModPack = "modpack";
    public const string ModLoader = "mod-loader";
    public const string Vehicle = "vehicle";
    public const string Save = "save";

    public static bool IsKnown(string value) =>
        value is Mod or ModPack or ModLoader or Vehicle or Save;
}

public static class ReleaseStatus
{
    public const string Stable = "stable";
    public const string Testing = "testing";
    public const string Dev = "dev";
}

/// <summary>
/// Dependency kinds, named after Debian's relationship kinds (RFC 0031 prior art).
/// </summary>
public static class DependencyKind
{
    public const string Required = "required";
    public const string Optional = "optional";
    public const string Recommends = "recommends";
    public const string Suggests = "suggests";
    public const string Conflict = "conflict";

    public static bool IsKnown(string value) =>
        value is Required or Optional or Recommends or Suggests or Conflict;
}

/// <summary>Where a dependency entry came from. Derived entries are ground truth (spec §12 check 17).</summary>
public static class DependencySource
{
    /// <summary>Read from the archive's own <c>[[StarMap.ModDependencies]]</c>.</summary>
    public const string Derived = "derived";

    /// <summary>Supplied by the author, adding the version bounds the loader cannot express.</summary>
    public const string Authored = "authored";
}

/// <summary>
/// The authored half of RFC 0031: what a human writes once, carrying facts that outlive any
/// single release. On ksamods.gg this is the listing, edited on the site rather than in a file,
/// and serialised to TOML only at export.
/// </summary>
public sealed record AuthoredDocument
{
    [JsonPropertyName("spec_version")] public int SpecVersion { get; init; } = 1;
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("authors")] public required IReadOnlyList<string> Authors { get; init; }
    [JsonPropertyName("abstract")] public required string Abstract { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("license")] public required string License { get; init; }
    [JsonPropertyName("tags")] public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>active or deprecated. The author's declaration about their own content.</summary>
    [JsonPropertyName("status")] public string Status { get; init; } = "active";

    /// <summary>
    /// The successor's id. A renamed mod is unavoidably a different mod because the id is the
    /// folder name, so without this every rename strands its users on a dead id. Deliberately
    /// not a dependency: "I am replaced by X" does not mean "I need X".
    /// </summary>
    [JsonPropertyName("superseded_by")] public string? SupersededBy { get; init; }

    /// <summary><c>forums</c> is required by RFC 0031 and therefore required to export.</summary>
    [JsonPropertyName("links")]
    public IReadOnlyDictionary<string, string> Links { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Where releases appear. Its presence is what tells RFC 0033's watcher it may stamp releases
    /// for this listing unattended; a listing without one takes the pull request path instead.
    /// It is also what ownership binds to, so omitting it moves the proof onto
    /// <c>links.repository</c>.
    /// </summary>
    [JsonPropertyName("releases")] public ReleasesBlock? Releases { get; init; }

    [JsonPropertyName("compatibility")] public CompatibilityBlock? Compatibility { get; init; }
    [JsonPropertyName("loader")] public LoaderBlock? Loader { get; init; }
    [JsonPropertyName("dependencies")] public IReadOnlyList<DependencyEntry> Dependencies { get; init; } = [];
}

/// <summary>
/// The hosts a listing's releases appear on (RFC 0031 §<c>[releases]</c>).
///
/// <para><c>authority</c> names which one is canonical and is only meaningful with more than one
/// host, so it stays absent in the single-host case rather than restating the obvious.</para>
/// </summary>
public sealed record ReleasesBlock
{
    /// <summary><c>owner/repo</c>. Not a URL: the watcher calls the API, not the web page.</summary>
    [JsonPropertyName("github")] public string? GitHub { get; init; }

    /// <summary>The numeric SpaceDock mod id.</summary>
    [JsonPropertyName("spacedock")] public long? SpaceDock { get; init; }

    [JsonPropertyName("authority")] public string? Authority { get; init; }

    public bool IsEmpty => GitHub is null && SpaceDock is null;
}

public sealed record CompatibilityBlock
{
    /// <summary>Oldest game version known to work, as displayed, or a month such as 2026.7.</summary>
    [JsonPropertyName("game_min")] public string? GameMin { get; init; }

    /// <summary>Newest tested version. Absent means no known upper limit - the recommended default.</summary>
    [JsonPropertyName("game_max")] public string? GameMax { get; init; }

    /// <summary>Platforms known to work. Absent means no known restriction.</summary>
    [JsonPropertyName("os")] public IReadOnlyList<string>? Os { get; init; }
}

/// <summary>
/// The loader a code mod needs. Bounds are authored because they cannot be derived: StarMap's
/// own <c>[StarMap]</c> section deliberately carries no versions.
/// </summary>
public sealed record LoaderBlock
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("min")] public required string Min { get; init; }
    [JsonPropertyName("max")] public string? Max { get; init; }
    [JsonPropertyName("source")] public string? Source { get; init; }
}

public sealed record DependencyEntry
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>Alternatives, satisfied by any one. Valid with kind required or recommends.</summary>
    [JsonPropertyName("any_of")] public IReadOnlyList<DependencyAlternative>? AnyOf { get; init; }

    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("min")] public string? Min { get; init; }
    [JsonPropertyName("max")] public string? Max { get; init; }
    [JsonPropertyName("source")] public string Source { get; init; } = DependencySource.Authored;
}

public sealed record DependencyAlternative
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("min")] public string? Min { get; init; }
    [JsonPropertyName("max")] public string? Max { get; init; }
}

/// <summary>
/// The generated half of RFC 0031: one document per release, stamped by tooling and immutable
/// afterwards apart from the narrow amendment set in backend.md §5.5.
/// </summary>
public sealed record ReleaseDocument
{
    [JsonPropertyName("spec_version")] public int SpecVersion { get; init; } = 1;
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("version")] public required string Version { get; init; }
    [JsonPropertyName("version_scheme")] public string VersionScheme { get; init; } = "semver";
    [JsonPropertyName("release_status")] public required string Status { get; init; }
    [JsonPropertyName("release_date")] public required DateTimeOffset ReleaseDate { get; init; }

    [JsonPropertyName("game_min")] public string? GameMin { get; init; }

    /// <summary>
    /// The resolved revision, stamped alongside the display string so a client can evaluate
    /// compatibility offline and for a release the index has not caught up with (RFC 0017).
    /// </summary>
    [JsonPropertyName("game_min_revision")] public int? GameMinRevision { get; init; }
    [JsonPropertyName("game_max")] public string? GameMax { get; init; }
    [JsonPropertyName("game_max_revision")] public int? GameMaxRevision { get; init; }
    [JsonPropertyName("os")] public IReadOnlyList<string>? Os { get; init; }

    [JsonPropertyName("download")] public required DownloadBlock Download { get; init; }
    [JsonPropertyName("install_size")] public long? InstallSize { get; init; }
    [JsonPropertyName("install")] public InstallBlock? Install { get; init; }
    [JsonPropertyName("loader")] public LoaderBlock? Loader { get; init; }
    [JsonPropertyName("dependencies")] public IReadOnlyList<DependencyEntry> Dependencies { get; init; } = [];
    [JsonPropertyName("changelog")] public string? Changelog { get; init; }

    /// <summary>
    /// How the listing described itself at stamp time, so browsing version 3 does not advertise
    /// what only arrived with version 4. Status and succession are deliberately excluded: those
    /// must reach every release the moment they are declared, so clients read them live.
    /// </summary>
    [JsonPropertyName("listing")] public ListingSnapshot? Listing { get; init; }

    [JsonPropertyName("yanked")] public bool? Yanked { get; init; }
    [JsonPropertyName("yanked_reason")] public string? YankedReason { get; init; }
}

public sealed record DownloadBlock
{
    [JsonPropertyName("url")] public required string Url { get; init; }
    [JsonPropertyName("mirrors")] public IReadOnlyList<string>? Mirrors { get; init; }
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
    [JsonPropertyName("size")] public required long Size { get; init; }
    [JsonPropertyName("content_type")] public string ContentType { get; init; } = "application/zip";
}

public sealed record InstallBlock
{
    /// <summary>The directory inside the archive whose contents become the installed folder.</summary>
    [JsonPropertyName("root")] public required string Root { get; init; }

    /// <summary>True when the standard layout was found rather than authored.</summary>
    [JsonPropertyName("derived")] public bool Derived { get; init; }
}

public sealed record ListingSnapshot
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("authors")] public required IReadOnlyList<string> Authors { get; init; }
    [JsonPropertyName("abstract")] public required string Abstract { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("license")] public required string License { get; init; }
    [JsonPropertyName("tags")] public IReadOnlyList<string> Tags { get; init; } = [];
    [JsonPropertyName("links")]
    public IReadOnlyDictionary<string, string> Links { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// A published modlist version. Pure reference metadata: no download, no checksum, no install
/// data. It never redistributes anyone's files - each member downloads from its own forge.
/// </summary>
public sealed record ModlistDocument
{
    [JsonPropertyName("spec_version")] public int SpecVersion { get; init; } = 1;
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("type")] public string Type { get; init; } = ContentType.ModPack;
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("authors")] public required IReadOnlyList<string> Authors { get; init; }
    [JsonPropertyName("abstract")] public required string Abstract { get; init; }
    [JsonPropertyName("license")] public required string License { get; init; }
    [JsonPropertyName("tags")] public IReadOnlyList<string> Tags { get; init; } = [];
    [JsonPropertyName("version")] public required string Version { get; init; }
    [JsonPropertyName("released_at")] public required DateTimeOffset ReleasedAt { get; init; }
    [JsonPropertyName("changelog")] public string? Changelog { get; init; }
    [JsonPropertyName("compatibility")] public CompatibilityBlock? Compatibility { get; init; }

    /// <summary>Exact pins, never ranges: a modlist is a curated, tested set.</summary>
    [JsonPropertyName("mods")] public IReadOnlyList<PinEntry> Mods { get; init; } = [];
    [JsonPropertyName("vehicles")] public IReadOnlyList<PinEntry> Vehicles { get; init; } = [];
    [JsonPropertyName("saves")] public IReadOnlyList<PinEntry> Saves { get; init; } = [];
}

public sealed record PinEntry
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("version")] public required string Version { get; init; }
}
