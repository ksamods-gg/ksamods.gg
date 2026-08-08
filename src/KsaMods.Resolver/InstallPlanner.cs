using KsaMods.Metadata;

namespace KsaMods.Resolver;

public sealed record ResolveTarget(string ModId, SemVer? PinnedVersion = null);

public sealed record ResolveRequest
{
    public required IReadOnlyList<ResolveTarget> Targets { get; init; }

    /// <summary>The installed game's revision. Null means the client could not detect one.</summary>
    public int? GameRevision { get; init; }

    /// <summary>RFC 0031: <c>recommends</c> is selected by default and deselectable by the user.</summary>
    public bool IncludeRecommended { get; init; } = true;
}

public enum ProblemKind
{
    UnknownMod,
    NoCompatibleVersion,
    BoundsUnsatisfiable,
    Conflict,
    PinnedVersionMissing,
}

public sealed record ResolveProblem(ProblemKind Kind, string ModId, string Detail);

public sealed record PlannedInstall
{
    public required string ModId { get; init; }
    public required SemVer Version { get; init; }
    public required CompatibilityState Compatibility { get; init; }

    /// <summary>Why this ended up in the plan: requested outright, or pulled in by something.</summary>
    public required string Reason { get; init; }

    public bool OverridesCore { get; init; }
}

/// <summary>Two selected releases declaring the same asset id. One of them will silently lose.</summary>
public sealed record AssetCollision(string AssetId, IReadOnlyList<string> ModIds);

public sealed record ResolvePlan
{
    public required bool Satisfiable { get; init; }

    /// <summary>
    /// The order a client writes into <c>manifest.toml</c>: dependencies before dependents,
    /// Core-overriding mods first.
    /// </summary>
    public IReadOnlyList<PlannedInstall> Order { get; init; } = [];

    public IReadOnlyList<AssetCollision> Collisions { get; init; } = [];
    public IReadOnlyList<ResolveProblem> Problems { get; init; } = [];

    /// <summary>Suggested but not selected, so a client can offer them.</summary>
    public IReadOnlyList<string> Suggested { get; init; } = [];
}

/// <summary>
/// Turns a wanted set into an ordered install plan (backend.md §10.4).
///
/// <para>This ships as a library as well as running behind <c>POST /resolve</c>. The offline
/// index export means clients must be able to solve locally, so a server-only solver would
/// merely guarantee a second, subtly different implementation appears - which is how ecosystems
/// get divergent resolution behaviour.</para>
///
/// <para><b>The returned order is not a promise about initialisation order.</b> StarMap walks the
/// manifest but defers mods with unmet dependencies through its waiting graph, so it reorders
/// initialisation within the walk. This is the order to write, not the order that will run.</para>
/// </summary>
public sealed class InstallPlanner(ICatalogue catalogue)
{
    private const int MaxFixpointIterations = 64;
    private const int MaxRejections = 256;

    public ResolvePlan Resolve(ResolveRequest request)
    {
        var problems = new List<ResolveProblem>();
        var roots = new Dictionary<string, VersionBound>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in request.Targets)
        {
            if (!catalogue.Exists(target.ModId))
            {
                // An unlisted dependency warns and proceeds rather than blocking - punishing the
                // user for someone else's missing listing helps nobody (backend.md §8).
                problems.Add(new ResolveProblem(ProblemKind.UnknownMod, target.ModId,
                    "Not listed in the index. It may still exist elsewhere."));
                continue;
            }

            var bound = target.PinnedVersion is null
                ? VersionBound.Any
                : new VersionBound(target.PinnedVersion, target.PinnedVersion);

            roots[target.ModId] = roots.TryGetValue(target.ModId, out var existing)
                ? existing.Intersect(bound)
                : bound;
        }

        if (roots.Count == 0)
        {
            return new ResolvePlan { Satisfiable = problems.Count == 0, Problems = problems };
        }

        // Backtracking happens here rather than inside the fixpoint: when a selected version
        // turns out to demand something unobtainable, that exact (mod, version) is struck out
        // and the whole thing is re-solved. Coarser than in-loop backtracking and far easier to
        // reason about - which matters, because a second implementation has to match this.
        var rejected = new HashSet<(string ModId, string Version)>();

        for (var attempt = 0; attempt < MaxRejections; attempt++)
        {
            var outcome = TrySolve(request, roots, rejected);

            if (outcome.Solved)
            {
                return Build(request, outcome, problems);
            }

            if (outcome.Blame is { } blame && rejected.Add((blame.ModId, blame.Version.ToString())))
            {
                continue;
            }

            problems.AddRange(outcome.Problems);
            return new ResolvePlan { Satisfiable = false, Problems = problems };
        }

        problems.Add(new ResolveProblem(ProblemKind.BoundsUnsatisfiable, roots.Keys.First(),
            "The dependency graph could not be resolved within the attempt budget."));
        return new ResolvePlan { Satisfiable = false, Problems = problems };
    }

    private sealed record Attempt
    {
        public required bool Solved { get; init; }
        public Dictionary<string, CatalogueRelease> Selected { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Reasons { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Suggested { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ResolveProblem> Problems { get; init; } = [];

        /// <summary>The release to strike out and retry without, when one can be identified.</summary>
        public CatalogueRelease? Blame { get; init; }
    }

    /// <summary>
    /// Iterates to a fixpoint: recollect every constraint from the roots and the currently
    /// selected releases, re-pick the best version for each, repeat until nothing moves.
    ///
    /// <para>Recollecting from scratch each pass is what makes constraints from a second
    /// dependent land in time. An earlier design walked the graph depth-first and merged bounds
    /// as it discovered them, which silently picked a too-new version whenever the tightening
    /// constraint happened to be discovered after the selection.</para>
    /// </summary>
    private Attempt TrySolve(
        ResolveRequest request,
        Dictionary<string, VersionBound> roots,
        HashSet<(string, string)> rejected)
    {
        var selected = new Dictionary<string, CatalogueRelease>(StringComparer.OrdinalIgnoreCase);
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var suggested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in roots.Keys) reasons[id] = "requested";

        for (var iteration = 0; iteration < MaxFixpointIterations; iteration++)
        {
            var bounds = new Dictionary<string, VersionBound>(roots, StringComparer.OrdinalIgnoreCase);
            var demandedBy = new Dictionary<string, CatalogueRelease>(StringComparer.OrdinalIgnoreCase);
            suggested.Clear();

            foreach (var release in selected.Values.ToList())
            {
                // The loader is not repeated under dependencies and the game is not a dependency;
                // both have their own sections. The loader still has to be installed.
                if (release.Loader is { } loader)
                {
                    Merge(bounds, loader.ModId, loader.Bound);
                    demandedBy.TryAdd(loader.ModId, release);
                    reasons.TryAdd(loader.ModId, $"loader for {release.ModId}");
                }

                foreach (var dependency in release.Dependencies)
                {
                    var effective = dependency.Kind switch
                    {
                        DependencyKind.Required => true,
                        DependencyKind.Recommends when request.IncludeRecommended => true,
                        _ => false,
                    };

                    if (dependency.Kind is DependencyKind.Suggests or DependencyKind.Recommends && !effective)
                    {
                        foreach (var id in Names(dependency)) suggested.Add(id);
                        continue;
                    }

                    if (dependency.Kind == DependencyKind.Optional)
                    {
                        // Used when present, not installed by default. If it is in the set for
                        // some other reason, the bound still applies.
                        foreach (var id in Names(dependency))
                        {
                            if (selected.ContainsKey(id)) Merge(bounds, id, dependency.Bound);
                        }
                        continue;
                    }

                    if (dependency.Kind == DependencyKind.Conflict) continue;
                    if (!effective) continue;

                    foreach (var (id, bound) in ChooseTargets(dependency, selected))
                    {
                        Merge(bounds, id, bound);
                        demandedBy.TryAdd(id, release);
                        reasons.TryAdd(id, $"required by {release.ModId}");
                    }
                }
            }

            // Drop anything no longer demanded - a dependent may have changed version and let go.
            foreach (var stale in selected.Keys.Where(k => !bounds.ContainsKey(k)).ToList())
            {
                selected.Remove(stale);
            }

            var changed = false;

            foreach (var (modId, bound) in bounds)
            {
                if (bound.IsEmpty)
                {
                    // Two dependents want disjoint ranges. There is no version to try, so this is
                    // reported rather than backtracked.
                    return Fail(new ResolveProblem(ProblemKind.BoundsUnsatisfiable, modId,
                        $"Version constraints from different dependents cannot be satisfied together ({bound})."));
                }

                var best = catalogue.ReleasesOf(modId)
                    .Where(r => !r.Yanked)
                    .Where(r => !rejected.Contains((r.ModId, r.Version.ToString())))
                    .Where(r => bound.Allows(r.Version))
                    // Incompatible is the one blocking state; Untested and Unknown stay, because
                    // the game validates nothing and a false "incompatible" takes a working mod
                    // away for no reason.
                    .Where(r => r.CompatibilityFor(request.GameRevision) != CompatibilityState.Incompatible)
                    .MaxBy(r => r.Version);

                if (best is null)
                {
                    var problem = Describe(modId, bound, request, rejected);

                    // If something pulled this in, that something is the thing to reconsider.
                    return demandedBy.TryGetValue(modId, out var culprit) && !roots.ContainsKey(culprit.ModId)
                        ? new Attempt { Solved = false, Blame = culprit, Problems = [problem] }
                        : demandedBy.TryGetValue(modId, out var rootCulprit)
                            ? new Attempt { Solved = false, Blame = rootCulprit, Problems = [problem] }
                            : Fail(problem);
                }

                if (!selected.TryGetValue(modId, out var current) || current.Version != best.Version)
                {
                    selected[modId] = best;
                    changed = true;
                }
            }

            if (changed) continue;

            // Stable. Conflicts are checked once, against the final set - checking them during
            // selection misses the case where the conflicting mod is chosen afterwards.
            var conflicts = FindConflicts(selected);
            if (conflicts.Count > 0)
            {
                return new Attempt { Solved = false, Problems = conflicts };
            }

            return new Attempt
            {
                Solved = true,
                Selected = selected,
                Reasons = reasons,
                Suggested = suggested,
            };
        }

        return Fail(new ResolveProblem(ProblemKind.BoundsUnsatisfiable, roots.Keys.First(),
            "Version selection did not stabilise."));
    }

    private static Attempt Fail(ResolveProblem problem) =>
        new() { Solved = false, Problems = [problem] };

    private ResolvePlan Build(ResolveRequest request, Attempt outcome, List<ResolveProblem> problems) =>
        new()
        {
            Satisfiable = true,
            Order = Order(outcome.Selected, outcome.Reasons, request.GameRevision),
            Collisions = FindCollisions(outcome.Selected),
            Problems = problems,
            Suggested =
            [
                .. outcome.Suggested
                    .Where(s => !outcome.Selected.ContainsKey(s))
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase),
            ],
        };

    /// <summary>
    /// Resolves one dependency entry to the ids it actually constrains.
    ///
    /// <para>An <c>any_of</c> group prefers a member already in the set, so a plan does not end
    /// up installing two implementations of the same thing; failing that it takes the first
    /// member the index knows about.</para>
    /// </summary>
    private IEnumerable<(string ModId, VersionBound Bound)> ChooseTargets(
        ResolvedDependency dependency, Dictionary<string, CatalogueRelease> selected)
    {
        if (!dependency.IsGroup)
        {
            if (dependency.ModId is not null) yield return (dependency.ModId, dependency.Bound);
            yield break;
        }

        var alternatives = dependency.Alternatives!;

        foreach (var alternative in alternatives)
        {
            if (selected.ContainsKey(alternative.ModId))
            {
                yield return alternative;
                yield break;
            }
        }

        foreach (var alternative in alternatives)
        {
            if (!catalogue.Exists(alternative.ModId)) continue;
            yield return alternative;
            yield break;
        }
    }

    private static void Merge(Dictionary<string, VersionBound> bounds, string modId, VersionBound bound) =>
        bounds[modId] = bounds.TryGetValue(modId, out var existing) ? existing.Intersect(bound) : bound;

    private static IEnumerable<string> Names(ResolvedDependency dependency) =>
        dependency.IsGroup
            ? dependency.Alternatives!.Select(a => a.ModId)
            : dependency.ModId is null ? [] : [dependency.ModId];

    private static List<ResolveProblem> FindConflicts(Dictionary<string, CatalogueRelease> selected)
    {
        var conflicts = new List<ResolveProblem>();

        foreach (var release in selected.Values)
        {
            foreach (var dependency in release.Dependencies)
            {
                if (dependency.Kind != DependencyKind.Conflict) continue;

                foreach (var id in Names(dependency))
                {
                    if (!selected.TryGetValue(id, out var other)) continue;
                    // No bounds means every version conflicts.
                    if (!dependency.Bound.Allows(other.Version)) continue;

                    conflicts.Add(new ResolveProblem(ProblemKind.Conflict, release.ModId,
                        $"{release.ModId} {release.Version} conflicts with {other.ModId} {other.Version}."));
                }
            }
        }

        return conflicts;
    }

    private ResolveProblem Describe(
        string modId, VersionBound bound, ResolveRequest request, HashSet<(string, string)> rejected)
    {
        var all = catalogue.ReleasesOf(modId);
        if (all.Count == 0) return new ResolveProblem(ProblemKind.UnknownMod, modId, "No releases are listed.");

        var live = all.Where(r => !r.Yanked && !rejected.Contains((r.ModId, r.Version.ToString()))).ToList();
        var inBound = live.Where(r => bound.Allows(r.Version)).ToList();

        if (inBound.Count == 0)
        {
            var pinned = bound is { Min: not null, Max: not null } && bound.Min == bound.Max;
            return new ResolveProblem(
                pinned ? ProblemKind.PinnedVersionMissing : ProblemKind.BoundsUnsatisfiable,
                modId,
                $"No release satisfies {bound}. Available: {string.Join(", ", live.Select(r => r.Version.ToString()))}.");
        }

        var needed = inBound.Min(r => r.GameMinRevision ?? 0);
        return new ResolveProblem(ProblemKind.NoCompatibleVersion, modId,
            request.GameRevision is null
                ? "No release is compatible."
                : $"Needs game revision {needed} or newer; you are on {request.GameRevision}.");
    }

    /// <summary>
    /// Topological order, dependencies first, with Core-overriding mods hoisted to the front.
    ///
    /// <para>Asset ids are first-wins, so a mod overriding stock content only works if it loads
    /// before <c>Core</c> - which means being above it in <c>manifest.toml</c>, a file the game
    /// rewrites freely. Fragile, and a client should say so.</para>
    /// </summary>
    private static List<PlannedInstall> Order(
        Dictionary<string, CatalogueRelease> selected,
        Dictionary<string, string> reasons,
        int? gameRevision)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<CatalogueRelease>();

        void Visit(string modId)
        {
            if (visited.Contains(modId)) return;

            // A dependency cycle must not hang the planner. Break it deterministically rather
            // than throwing: the plan is still useful, and StarMap's own waiting graph will make
            // what sense of it it can.
            if (!visiting.Add(modId)) return;

            if (selected.TryGetValue(modId, out var release))
            {
                if (release.Loader is { } loader) Visit(loader.ModId);

                foreach (var dependency in release.Dependencies)
                {
                    if (dependency.Kind == DependencyKind.Conflict) continue;
                    foreach (var id in Names(dependency))
                    {
                        if (selected.ContainsKey(id)) Visit(id);
                    }
                }

                ordered.Add(release);
            }

            visiting.Remove(modId);
            visited.Add(modId);
        }

        foreach (var modId in selected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)) Visit(modId);

        return
        [
            .. ordered
                .OrderByDescending(r => r.OverridesCore)
                .Select(r => new PlannedInstall
                {
                    ModId = r.ModId,
                    Version = r.Version,
                    Compatibility = r.CompatibilityFor(gameRevision),
                    Reason = reasons.GetValueOrDefault(r.ModId, "dependency"),
                    OverridesCore = r.OverridesCore,
                }),
        ];
    }

    /// <summary>
    /// Asset ids declared by more than one selected release.
    ///
    /// <para>KSA registers ids with <c>TryAdd</c> into one global table, so the loser is silently
    /// discarded with no error and no log line a user will find. Returning this alongside the plan
    /// is what lets a client warn before it writes anything.</para>
    /// </summary>
    private static List<AssetCollision> FindCollisions(Dictionary<string, CatalogueRelease> selected)
    {
        var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var release in selected.Values)
        {
            foreach (var assetId in release.AssetIds)
            {
                if (!owners.TryGetValue(assetId, out var list))
                {
                    list = [];
                    owners[assetId] = list;
                }
                if (!list.Contains(release.ModId, StringComparer.OrdinalIgnoreCase)) list.Add(release.ModId);
            }
        }

        return
        [
            .. owners
                .Where(kv => kv.Value.Count > 1)
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new AssetCollision(
                    kv.Key,
                    [.. kv.Value.OrderBy(v => v, StringComparer.OrdinalIgnoreCase)])),
        ];
    }
}
