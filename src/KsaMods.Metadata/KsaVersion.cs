using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace KsaMods.Metadata;

/// <summary>
/// A KSA game version: Year.Month.Build.Revision, optionally -Suffix and +hash.
///
/// <para><b>Ordering is by the revision alone</b> (RFC 0017, spec §16). Not the dotted string.
/// The third component is a counter local to whichever machine produced the build: across the
/// 155 shipped releases it <i>decreases</i> 32 times while the revision rises, only 46 distinct
/// values appear, and one is reused 16 times. Sorting the full four-part string puts 21 adjacent
/// pairs in the wrong order; sorting by revision alone reproduces the true order for all 155.</para>
///
/// <para>The game agrees: <c>VersionInfo.CompareTo</c> compares revision first, the build counter
/// only as a tiebreak, then the suffix. Year and month are display only.</para>
/// </summary>
public sealed partial class KsaVersion : IComparable<KsaVersion>, IEquatable<KsaVersion>
{
    // The shape the game itself accepts (KSA.VersionInfo). A leading v, a -suffix and a +hash
    // are all valid input.
    [GeneratedRegex(
        @"^v?(?<Year>\d+)\.(?<Month>\d+)\.(?<Build>\d+)\.(?<Revision>\d+)(?:-(?<Suffix>[^+]+))?(?:\+.*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern { get; }

    // A month bound: "2026.7" means the whole of that calendar month.
    [GeneratedRegex(@"^v?(?<Year>\d+)\.(?<Month>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex MonthPattern { get; }

    public int Year { get; }
    public int Month { get; }

    /// <summary>Machine-local build counter. Meaningless for ordering; kept because it is displayed.</summary>
    public int Build { get; }

    /// <summary>Commit count on main. The only component that orders.</summary>
    public int Revision { get; }

    public string? Suffix { get; }

    private KsaVersion(int year, int month, int build, int revision, string? suffix)
    {
        Year = year;
        Month = month;
        Build = build;
        Revision = revision;
        Suffix = suffix;
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out KsaVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var m = VersionPattern.Match(value.Trim());
        if (!m.Success) return false;

        // Anything that does not parse yields no version, and whatever carried it becomes
        // Unknown rather than being discarded (RFC 0017).
        if (!int.TryParse(m.Groups["Year"].ValueSpan, out var year) ||
            !int.TryParse(m.Groups["Month"].ValueSpan, out var month) ||
            !int.TryParse(m.Groups["Build"].ValueSpan, out var build) ||
            !int.TryParse(m.Groups["Revision"].ValueSpan, out var revision))
        {
            return false;
        }

        var suffix = m.Groups["Suffix"].Success ? m.Groups["Suffix"].Value : null;
        version = new KsaVersion(year, month, build, revision, suffix);
        return true;
    }

    public static KsaVersion Parse(string value) =>
        TryParse(value, out var v) ? v : throw new FormatException($"'{value}' is not a valid KSA version.");

    /// <summary>
    /// Parses a year-month prefix such as "2026.7". Only two granularities are worth offering,
    /// a month or an explicit revision: 20 of the 133 distinct Year.Month.Build combinations
    /// match more than one release, and 19 of those select a set that is not contiguous in time.
    /// </summary>
    public static bool TryParseMonth(string? value, out (int Year, int Month) month)
    {
        month = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var m = MonthPattern.Match(value.Trim());
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups["Year"].ValueSpan, out var y)) return false;
        if (!int.TryParse(m.Groups["Month"].ValueSpan, out var mo)) return false;
        if (mo is < 1 or > 12) return false;

        month = (y, mo);
        return true;
    }

    /// <summary>
    /// Mirrors <c>KSA.VersionInfo.CompareTo</c>: revision, then the build counter only as a
    /// tiebreak, then the suffix ordinally. Year and month never participate.
    /// </summary>
    public int CompareTo(KsaVersion? other)
    {
        if (other is null) return 1;

        var byRevision = Revision.CompareTo(other.Revision);
        if (byRevision != 0) return byRevision;

        var byBuild = Build.CompareTo(other.Build);
        if (byBuild != 0) return byBuild;

        return string.CompareOrdinal(Suffix ?? string.Empty, other.Suffix ?? string.Empty);
    }

    /// <summary>
    /// Renders the version the way the game shows it, build counter included. It is meaningless
    /// for ordering but it is what the user sees in-game, in the launcher and in a bug report,
    /// and rendering something else invites confusion (RFC 0017).
    /// </summary>
    public string ToDisplayString() =>
        Suffix is null
            ? $"{Year}.{Month}.{Build}.{Revision}"
            : $"{Year}.{Month}.{Build}.{Revision}-{Suffix}";

    public override string ToString() => ToDisplayString();

    public bool Equals(KsaVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is KsaVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Revision, Build, Suffix);
}

/// <summary>The four compatibility states of RFC 0017. Only <see cref="Incompatible"/> blocks.</summary>
public enum CompatibilityState
{
    /// <summary>No usable lower bound. Listed, installable after confirmation.</summary>
    Unknown,

    /// <summary>Installed revision is below the declared minimum. The only blocking state.</summary>
    Incompatible,

    /// <summary>Within the declared range.</summary>
    Compatible,

    /// <summary>Above a declared upper bound. Installs after confirmation.</summary>
    Untested,
}

public static class Compatibility
{
    /// <summary>
    /// Evaluates a release against an installed revision.
    ///
    /// <para>The asymmetry is deliberate and load-bearing: the game validates nothing, so a false
    /// "incompatible" takes a working mod away from the user for no reason, while a false
    /// "compatible" is a mod that does not load and can be removed again. Warn, do not block.</para>
    /// </summary>
    public static CompatibilityState Evaluate(int installedRevision, int? minRevision, int? maxRevision)
    {
        if (minRevision is null) return CompatibilityState.Unknown;
        if (installedRevision < minRevision.Value) return CompatibilityState.Incompatible;
        if (maxRevision is null || installedRevision <= maxRevision.Value) return CompatibilityState.Compatible;
        return CompatibilityState.Untested;
    }

    public static bool Blocks(this CompatibilityState state) => state == CompatibilityState.Incompatible;
}
