using KsaMods.Metadata;

namespace KsaMods.Api.Domain;

/// <summary>The mutable surface of a published release. Everything else is frozen.</summary>
public sealed record ReleaseFacts
{
    public int? GameMinRevision { get; init; }
    public int? GameMaxRevision { get; init; }
    public string? LoaderMin { get; init; }
    public string? LoaderMax { get; init; }
    public IReadOnlyList<DependencyFact> Dependencies { get; init; } = [];
    public bool Yanked { get; init; }
    public string? YankedReason { get; init; }
}

public sealed record DependencyFact
{
    public required string DepId { get; init; }
    public required string Kind { get; init; }
    public string? Min { get; init; }
    public string? Max { get; init; }
}

public sealed record AmendmentRejection(string Field, string Reason);

/// <summary>
/// Enforces RFC 0031's post-publish amendment rules (backend.md §5.5).
///
/// <para><b>The invariant: a release can never become more permissive after publication.</b>
/// Every permitted amendment records knowledge gained after release, and every one narrows.
/// A release that turns out to support <i>more</i> than it was stamped with keeps its stamp,
/// because nobody re-verified the wider claim against the actual archive.</para>
///
/// <para>This is what makes "immutable" mean something in a system where a maintainer could
/// otherwise widen a bound with a PATCH.</para>
/// </summary>
public static class ReleaseAmendment
{
    public static IReadOnlyList<AmendmentRejection> Check(ReleaseFacts current, ReleaseFacts proposed)
    {
        var rejections = new List<AmendmentRejection>();

        // ── game compatibility: may only narrow ──
        // Raising the floor and lowering the ceiling both shrink the supported range.
        if (Widens(current.GameMinRevision, proposed.GameMinRevision, lowerIsWider: true))
        {
            rejections.Add(new AmendmentRejection("game_min_revision",
                "A minimum may be raised but never lowered: lowering claims support nobody re-verified."));
        }

        if (Widens(current.GameMaxRevision, proposed.GameMaxRevision, lowerIsWider: false))
        {
            rejections.Add(new AmendmentRejection("game_max_revision",
                "A maximum may be added or lowered but never raised or removed."));
        }

        // ── loader bounds: same rule ──
        if (WidensVersion(current.LoaderMin, proposed.LoaderMin, lowerIsWider: true))
        {
            rejections.Add(new AmendmentRejection("loader.min", "A loader minimum may be added or raised, never lowered or removed."));
        }

        if (WidensVersion(current.LoaderMax, proposed.LoaderMax, lowerIsWider: false))
        {
            rejections.Add(new AmendmentRejection("loader.max", "A loader maximum may be added or lowered, never raised or removed."));
        }

        // ── dependencies: entries may be added and bounds tightened; never removed or widened ──
        var currentById = current.Dependencies.ToDictionary(d => d.DepId, StringComparer.OrdinalIgnoreCase);
        var proposedById = proposed.Dependencies.ToDictionary(d => d.DepId, StringComparer.OrdinalIgnoreCase);

        foreach (var (id, existing) in currentById)
        {
            if (!proposedById.TryGetValue(id, out var updated))
            {
                // Removing an entry is never an amendment. A derived entry especially: the loader
                // acts on it at runtime whatever the index says.
                rejections.Add(new AmendmentRejection($"dependencies[{id}]",
                    "A dependency entry cannot be removed after publication."));
                continue;
            }

            if (!string.Equals(existing.Kind, updated.Kind, StringComparison.Ordinal))
            {
                rejections.Add(new AmendmentRejection($"dependencies[{id}].kind",
                    "A dependency kind cannot change after publication."));
            }

            if (WidensVersion(existing.Min, updated.Min, lowerIsWider: true))
            {
                rejections.Add(new AmendmentRejection($"dependencies[{id}].min",
                    "A dependency minimum may be added or raised, never lowered or removed."));
            }

            if (WidensVersion(existing.Max, updated.Max, lowerIsWider: false))
            {
                rejections.Add(new AmendmentRejection($"dependencies[{id}].max",
                    "A dependency maximum may be added or lowered, never raised or removed."));
            }
        }

        // Un-yanking is deliberately not an amendment: a yank is a statement that this build is
        // bad, and reversing it silently would let a compromised build return without review.
        if (current.Yanked && !proposed.Yanked)
        {
            rejections.Add(new AmendmentRejection("yanked",
                "A yank cannot be reversed. Publish a new version instead."));
        }

        return rejections;
    }

    /// <summary>
    /// True when a bound moved in the widening direction, including being removed entirely.
    /// Adding a bound where there was none always narrows, so it is always allowed.
    /// </summary>
    private static bool Widens(int? current, int? proposed, bool lowerIsWider)
    {
        if (current is null) return false;          // adding a bound narrows
        if (proposed is null) return true;          // removing one widens

        return lowerIsWider ? proposed < current : proposed > current;
    }

    private static bool WidensVersion(string? current, string? proposed, bool lowerIsWider)
    {
        if (current is null) return false;
        if (proposed is null) return true;

        if (!SemVer.TryParse(current, out var a) || !SemVer.TryParse(proposed, out var b))
        {
            // An unparseable bound cannot be compared, so it cannot be shown to narrow.
            return !string.Equals(current, proposed, StringComparison.Ordinal);
        }

        return lowerIsWider ? b < a : b > a;
    }
}
