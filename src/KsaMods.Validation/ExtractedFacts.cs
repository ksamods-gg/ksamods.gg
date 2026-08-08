using KsaMods.Metadata;

namespace KsaMods.Validation;

/// <summary>
/// Everything the validator learned from the bytes.
///
/// <para><b>This is persisted whole, on first contact.</b> The site does not keep the archive
/// (backend.md §5.3 step 7) and asset URLs rot, so a fact not extracted now may be unrecoverable
/// later. Stages 6 and 7b run from the first import even though nothing consumes them until a
/// much later phase, for exactly this reason.</para>
/// </summary>
public sealed record ExtractedFacts
{
    public string? InstallRoot { get; init; }
    public bool InstallRootDerived { get; init; }

    /// <summary>Stage 6. Feeds the cross-mod collision index, which is this project's distinctive contribution.</summary>
    public IReadOnlyList<AssetIdRef> AssetIds { get; init; } = [];

    /// <summary>Stage 7b. Ground truth for code dependencies: the loader acts on these at runtime.</summary>
    public IReadOnlyList<StarMapDependency> Dependencies { get; init; } = [];

    /// <summary>Stage 7.</summary>
    public IReadOnlyList<ShippedAssembly> Assemblies { get; init; } = [];

    /// <summary>Stage 8. Displayed verbatim on the listing; never silently stripped.</summary>
    public IReadOnlyList<ConsoleCommand> ConsoleCommands { get; init; } = [];

    /// <summary>Paths named in the six list keys of mod.toml.</summary>
    public IReadOnlyList<string> DeclaredPaths { get; init; } = [];

    /// <summary>Declared paths plus everything reachable from inside their XML, transitively.</summary>
    public IReadOnlyList<string> ReachablePaths { get; init; } = [];

    public long UncompressedSize { get; init; }
    public string? EntryAssembly { get; init; }
    public string? ModName { get; init; }
    public string? CommunityVersion { get; init; }
    public string? CommunityAuthor { get; init; }

    public bool ShipsCode => Assemblies.Count > 0;
}

public sealed record AssetIdRef(string Id, string XmlPath);

public sealed record ShippedAssembly
{
    public required string FilePath { get; init; }
    public string? AssemblyName { get; init; }
    public string? AssemblyVersion { get; init; }
    public bool IsEntry { get; init; }
}

public sealed record ConsoleCommand(string Hook, int Ordinal, string Command);

public sealed record ValidationResult
{
    public required ValidationOutcome Outcome { get; init; }
    public required IReadOnlyList<Finding> Findings { get; init; }
    public required ExtractedFacts Facts { get; init; }

    public IEnumerable<Finding> Errors => Findings.Where(f => f.Severity == Severity.Error);
    public IEnumerable<Finding> Warnings => Findings.Where(f => f.Severity == Severity.Warning);
}
