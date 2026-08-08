using KsaMods.Metadata;

namespace KsaMods.Resolver;

/// <summary>An inclusive version bound. Two typed fields, never a range expression.</summary>
/// <remarks>
/// RFC 0031 rejects expressions such as <c>&gt;=0.1.0 &lt;0.2.0</c> deliberately: an expression
/// needs a parser whose edge cases we would have to specify ourselves, and npm, cargo and NuGet
/// all differ subtly on exactly those edges.
/// </remarks>
public sealed record VersionBound(SemVer? Min, SemVer? Max)
{
    public static readonly VersionBound Any = new(null, null);

    public bool Allows(SemVer version) =>
        (Min is null || version >= Min) && (Max is null || version <= Max);

    /// <summary>The tightest bound satisfying both. Used when several dependents constrain one mod.</summary>
    public VersionBound Intersect(VersionBound other)
    {
        var min = (Min, other.Min) switch
        {
            (null, var b) => b,
            (var a, null) => a,
            var (a, b) => a! >= b! ? a : b,
        };

        var max = (Max, other.Max) switch
        {
            (null, var b) => b,
            (var a, null) => a,
            var (a, b) => a! <= b! ? a : b,
        };

        return new VersionBound(min, max);
    }

    public bool IsEmpty => Min is not null && Max is not null && Min > Max;

    public override string ToString() => (Min, Max) switch
    {
        (null, null) => "any",
        (var a, null) => $"≥{a}",
        (null, var b) => $"≤{b}",
        var (a, b) => $"{a}–{b}",
    };
}

public sealed record ResolvedDependency
{
    /// <summary>Null when this entry is an <c>any_of</c> group; see <see cref="Alternatives"/>.</summary>
    public string? ModId { get; init; }

    /// <summary>Alternatives satisfied by any one member. Valid with required or recommends.</summary>
    public IReadOnlyList<(string ModId, VersionBound Bound)>? Alternatives { get; init; }

    public required string Kind { get; init; }
    public VersionBound Bound { get; init; } = VersionBound.Any;

    /// <summary>
    /// Derived entries come from the archive's own <c>[[StarMap.ModDependencies]]</c> and are
    /// ground truth: the loader acts on them at runtime whatever the index says.
    /// </summary>
    public string Source { get; init; } = DependencySource.Derived;

    public bool IsGroup => Alternatives is { Count: > 0 };
}

public sealed record LoaderRequirement(string ModId, VersionBound Bound);

/// <summary>One installable release, as the resolver sees it.</summary>
public sealed record CatalogueRelease
{
    public required string ModId { get; init; }
    public required SemVer Version { get; init; }

    public int? GameMinRevision { get; init; }
    public int? GameMaxRevision { get; init; }

    public LoaderRequirement? Loader { get; init; }
    public IReadOnlyList<ResolvedDependency> Dependencies { get; init; } = [];

    /// <summary>Asset ids this release declares. Feeds collision detection.</summary>
    public IReadOnlyList<string> AssetIds { get; init; } = [];

    public bool Yanked { get; init; }

    /// <summary>True when the mod overrides stock content and must load before <c>Core</c>.</summary>
    public bool OverridesCore { get; init; }

    public CompatibilityState CompatibilityFor(int? gameRevision) =>
        gameRevision is null
            ? CompatibilityState.Unknown
            : Compatibility.Evaluate(gameRevision.Value, GameMinRevision, GameMaxRevision);
}

/// <summary>What the resolver reads. Backed by Postgres in the API, by a dictionary in tests.</summary>
public interface ICatalogue
{
    /// <summary>Every non-yanked release of a mod, in any order. Empty when the id is unknown.</summary>
    IReadOnlyList<CatalogueRelease> ReleasesOf(string modId);

    bool Exists(string modId);
}

public sealed class InMemoryCatalogue : ICatalogue
{
    private readonly Dictionary<string, List<CatalogueRelease>> _byMod = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryCatalogue Add(CatalogueRelease release)
    {
        if (!_byMod.TryGetValue(release.ModId, out var list))
        {
            list = [];
            _byMod[release.ModId] = list;
        }
        list.Add(release);
        return this;
    }

    public IReadOnlyList<CatalogueRelease> ReleasesOf(string modId) =>
        _byMod.TryGetValue(modId, out var list) ? list : [];

    public bool Exists(string modId) => _byMod.ContainsKey(modId);
}
