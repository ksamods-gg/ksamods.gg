using KsaMods.Metadata;

namespace KsaMods.Api.Domain;

/// <summary>One entry on the mutable draft. A null version means "current at publish time".</summary>
public sealed record DraftEntry(string Kind, string TargetId, string? PinnedVersion, int Position, string? Note);

/// <summary>What the publisher knows about a candidate member.</summary>
public sealed record MemberRelease
{
    public required string ModId { get; init; }
    public required string Version { get; init; }
    public int? GameMinRevision { get; init; }
    public int? GameMaxRevision { get; init; }
    public bool Yanked { get; init; }
    public string ListingState { get; init; } = "listed";
    public string Status { get; init; } = "active";
    public IReadOnlyList<string> AssetIds { get; init; } = [];
    public IReadOnlyList<string> RequiredDependencies { get; init; } = [];
}

public enum PublishIssueKind
{
    EmptyList,
    UnknownMember,
    PinnedVersionMissing,
    MemberYanked,
    MemberDelisted,
    MemberDeprecated,
    AssetCollision,
    NoCommonCompatibility,
    MissingDependency,
}

public sealed record PublishIssue(PublishIssueKind Kind, bool Blocking, string Detail);

public sealed record PublishResult
{
    public required bool CanPublish { get; init; }
    public IReadOnlyList<PublishIssue> Issues { get; init; } = [];
    public IReadOnlyList<PinEntry> Mods { get; init; } = [];
    public IReadOnlyList<PinEntry> Vehicles { get; init; } = [];
    public IReadOnlyList<PinEntry> Saves { get; init; } = [];
    public int? GameMinRevision { get; init; }
    public int? GameMaxRevision { get; init; }

    /// <summary>True when warnings exist that the publisher must explicitly acknowledge.</summary>
    public bool NeedsConfirmation => Issues.Any(i => !i.Blocking);
}

/// <summary>
/// Turns a draft into an immutable published version (backend.md §6.1, §6.2).
///
/// <para>Pure, so the publish rules are testable without a database — which matters, because
/// this is where "curated and tested" either means something or does not.</para>
/// </summary>
public static class ModlistPublish
{
    public static PublishResult Prepare(
        IReadOnlyList<DraftEntry> draft,
        IReadOnlyDictionary<string, IReadOnlyList<MemberRelease>> catalogue,
        bool confirmed)
    {
        var issues = new List<PublishIssue>();

        if (draft.Count == 0)
        {
            return new PublishResult
            {
                CanPublish = false,
                Issues = [new PublishIssue(PublishIssueKind.EmptyList, true, "A modlist must contain at least one entry.")],
            };
        }

        var resolved = new List<(DraftEntry Entry, MemberRelease Release)>();

        foreach (var entry in draft.OrderBy(e => e.Position))
        {
            if (!catalogue.TryGetValue(entry.TargetId, out var releases) || releases.Count == 0)
            {
                issues.Add(new PublishIssue(PublishIssueKind.UnknownMember, true,
                    $"'{entry.TargetId}' is not listed in the index."));
                continue;
            }

            MemberRelease? chosen;
            if (entry.PinnedVersion is not null)
            {
                chosen = releases.FirstOrDefault(r =>
                    string.Equals(r.Version, entry.PinnedVersion, StringComparison.Ordinal));

                if (chosen is null)
                {
                    issues.Add(new PublishIssue(PublishIssueKind.PinnedVersionMissing, true,
                        $"'{entry.TargetId}' has no version {entry.PinnedVersion}."));
                    continue;
                }
            }
            else
            {
                // An unpinned entry resolves to the current best at publish time. The resulting
                // pin is always exact: a published modlist never contains a floating reference.
                chosen = releases
                    .Where(r => !r.Yanked)
                    .MaxBy(r => SemVer.TryParse(r.Version, out var v) ? v : null);

                if (chosen is null)
                {
                    issues.Add(new PublishIssue(PublishIssueKind.UnknownMember, true,
                        $"'{entry.TargetId}' has no publishable release."));
                    continue;
                }
            }

            // Warnings below: overridable, because the members may still be individually
            // installable and RFC 0017's line is that only Incompatible blocks.
            if (chosen.Yanked)
            {
                issues.Add(new PublishIssue(PublishIssueKind.MemberYanked, false,
                    $"'{entry.TargetId}' {chosen.Version} has been yanked by its author."));
            }

            if (chosen.ListingState is "delisted" or "taken_down")
            {
                issues.Add(new PublishIssue(PublishIssueKind.MemberDelisted, false,
                    $"'{entry.TargetId}' has been {chosen.ListingState} by moderators."));
            }

            if (chosen.Status == "deprecated")
            {
                issues.Add(new PublishIssue(PublishIssueKind.MemberDeprecated, false,
                    $"'{entry.TargetId}' is deprecated by its author."));
            }

            resolved.Add((entry, chosen));
        }

        if (issues.Any(i => i.Blocking))
        {
            return new PublishResult { CanPublish = false, Issues = issues };
        }

        issues.AddRange(FindCollisions(resolved));
        issues.AddRange(FindMissingDependencies(resolved));

        var (min, max, compatible) = ComputeCompatibility(resolved);
        if (!compatible)
        {
            issues.Add(new PublishIssue(PublishIssueKind.NoCommonCompatibility, false,
                $"No game build satisfies every member: the highest minimum ({min}) is above the lowest maximum ({max})."));
        }

        var pins = resolved
            .GroupBy(r => r.Entry.Kind, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PinEntry>)
                [
                    .. g.OrderBy(r => r.Entry.Position)
                        .Select(r => new PinEntry { Id = r.Release.ModId, Version = r.Release.Version }),
                ],
                StringComparer.Ordinal);

        var warnings = issues.Count(i => !i.Blocking);

        return new PublishResult
        {
            // Warnings are overridable, but only deliberately: the publisher has to say so.
            CanPublish = warnings == 0 || confirmed,
            Issues = issues,
            Mods = pins.GetValueOrDefault("mod", []),
            Vehicles = pins.GetValueOrDefault("vehicle", []),
            Saves = pins.GetValueOrDefault("save", []),
            GameMinRevision = min,
            GameMaxRevision = max,
        };
    }

    /// <summary>
    /// A modlist works only where every member works, so the range is the intersection: the
    /// highest minimum and the lowest maximum. Computed, never authored.
    /// </summary>
    private static (int? Min, int? Max, bool Compatible) ComputeCompatibility(
        List<(DraftEntry Entry, MemberRelease Release)> resolved)
    {
        int? min = null;
        int? max = null;

        foreach (var (_, release) in resolved)
        {
            if (release.GameMinRevision is { } candidateMin && (min is null || candidateMin > min)) min = candidateMin;
            if (release.GameMaxRevision is { } candidateMax && (max is null || candidateMax < max)) max = candidateMax;
        }

        return (min, max, min is null || max is null || min <= max);
    }

    /// <summary>
    /// Asset ids declared by more than one member. KSA registers ids with <c>TryAdd</c>, so one
    /// of them will silently vanish and the user will have no idea why.
    /// </summary>
    private static IEnumerable<PublishIssue> FindCollisions(
        List<(DraftEntry Entry, MemberRelease Release)> resolved)
    {
        var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (_, release) in resolved)
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

        return owners
            .Where(kv => kv.Value.Count > 1)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new PublishIssue(PublishIssueKind.AssetCollision, false,
                $"Asset id '{kv.Key}' is declared by {string.Join(" and ", kv.Value)}; only the first to load will register."));
    }

    /// <summary>
    /// The check that earns curation its name. The game resolves nothing and StarMap fails
    /// silently to a console nobody reads, so a modlist missing a dependency ships a mod that
    /// never loads and never says why.
    /// </summary>
    private static IEnumerable<PublishIssue> FindMissingDependencies(
        List<(DraftEntry Entry, MemberRelease Release)> resolved)
    {
        var present = resolved
            .Select(r => r.Release.ModId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, release) in resolved)
        {
            foreach (var dependency in release.RequiredDependencies)
            {
                if (present.Contains(dependency)) continue;

                yield return new PublishIssue(PublishIssueKind.MissingDependency, false,
                    $"'{release.ModId}' requires '{dependency}', which is not in this list.");
            }
        }
    }
}
