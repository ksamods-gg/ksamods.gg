using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace KsaMods.Metadata;

/// <summary>
/// A release version, normalised to SemVer 2.0.0 (RFC 0031). A leading <c>v</c> on a forge tag is
/// stripped; a version that does not parse rejects the release at import time with the error in
/// front of the author, rather than being coerced into something plausible.
/// </summary>
public sealed partial class SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    [GeneratedRegex(
        @"^v?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)" +
        @"(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?" +
        @"(?:\+(?<buildmeta>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern { get; }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>Dot-separated pre-release identifiers, empty when this is a stable release.</summary>
    public IReadOnlyList<string> PreRelease { get; }

    /// <summary>Build metadata. Carried for display; ignored for ordering, per SemVer 2.0.0.</summary>
    public string? BuildMetadata { get; }

    public bool IsPreRelease => PreRelease.Count > 0;

    private SemVer(int major, int minor, int patch, IReadOnlyList<string> preRelease, string? buildMetadata)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
        BuildMetadata = buildMetadata;
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out SemVer? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var m = Pattern.Match(value.Trim());
        if (!m.Success) return false;

        if (!int.TryParse(m.Groups["major"].ValueSpan, out var major) ||
            !int.TryParse(m.Groups["minor"].ValueSpan, out var minor) ||
            !int.TryParse(m.Groups["patch"].ValueSpan, out var patch))
        {
            return false;
        }

        var pre = m.Groups["prerelease"].Success
            ? m.Groups["prerelease"].Value.Split('.')
            : [];

        version = new SemVer(major, minor, patch, pre, m.Groups["buildmeta"].Success ? m.Groups["buildmeta"].Value : null);
        return true;
    }

    public static SemVer Parse(string value) =>
        TryParse(value, out var v) ? v : throw new FormatException($"'{value}' is not a valid SemVer 2.0.0 version.");

    /// <summary>The normalised string: leading <c>v</c> stripped, build metadata preserved.</summary>
    public string Normalised
    {
        get
        {
            var sb = new StringBuilder(24);
            sb.Append(Major).Append('.').Append(Minor).Append('.').Append(Patch);
            if (IsPreRelease) sb.Append('-').Append(string.Join('.', PreRelease));
            if (BuildMetadata is not null) sb.Append('+').Append(BuildMetadata);
            return sb.ToString();
        }
    }

    public override string ToString() => Normalised;

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;

        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // A pre-release version has lower precedence than the associated normal version.
        if (!IsPreRelease && !other.IsPreRelease) return 0;
        if (!IsPreRelease) return 1;
        if (!other.IsPreRelease) return -1;

        var count = Math.Min(PreRelease.Count, other.PreRelease.Count);
        for (var i = 0; i < count; i++)
        {
            c = ComparePreReleaseIdentifier(PreRelease[i], other.PreRelease[i]);
            if (c != 0) return c;
        }

        // A larger set of pre-release fields has higher precedence, all else equal.
        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    private static int ComparePreReleaseIdentifier(string a, string b)
    {
        var aNumeric = IsNumeric(a);
        var bNumeric = IsNumeric(b);

        // Numeric identifiers always have lower precedence than alphanumeric ones, and numeric
        // identifiers compare numerically - which is why 1.0.0-alpha.10 sorts above
        // 1.0.0-alpha.2 despite sorting below it as text.
        if (aNumeric && bNumeric) return ulong.Parse(a).CompareTo(ulong.Parse(b));
        if (aNumeric) return -1;
        if (bNumeric) return 1;
        return string.CompareOrdinal(a, b);
    }

    private static bool IsNumeric(string s)
    {
        foreach (var c in s)
        {
            if (c is < '0' or > '9') return false;
        }
        return s.Length > 0;
    }

    /// <summary>
    /// A byte key that sorts identically to <see cref="CompareTo"/> under plain <c>bytea</c>
    /// comparison, so Postgres can order versions without a function call (backend.md §3.1).
    ///
    /// <para>SemVer precedence is not expressible in SQL collation, and doing it in the
    /// application on every query makes "list versions, newest first" the slowest endpoint on
    /// the site. This is computed once on insert.</para>
    ///
    /// <para>Layout: major|minor|patch as big-endian uint32, then a stable-release marker
    /// (0x01 for stable so it outranks any pre-release's 0x00), then each pre-release identifier
    /// length-prefixed and tagged numeric-vs-alphanumeric.</para>
    /// </summary>
    public byte[] ToSortKey()
    {
        var buffer = new MemoryStream(32);

        WriteBigEndian(buffer, (uint)Major);
        WriteBigEndian(buffer, (uint)Minor);
        WriteBigEndian(buffer, (uint)Patch);

        // Stable outranks every pre-release of the same core version.
        buffer.WriteByte(IsPreRelease ? (byte)0x00 : (byte)0x01);

        foreach (var identifier in PreRelease)
        {
            if (IsNumeric(identifier))
            {
                // Tag 0x00: numeric identifiers rank below alphanumeric ones.
                buffer.WriteByte(0x00);
                WriteBigEndian(buffer, ulong.Parse(identifier));
            }
            else
            {
                buffer.WriteByte(0x01);
                var bytes = Encoding.UTF8.GetBytes(identifier);
                buffer.Write(bytes, 0, bytes.Length);
            }
            // Separator below every byte a payload can produce, so a shorter identifier list
            // sorts before a longer one sharing its prefix.
            buffer.WriteByte(0x00);
        }

        return buffer.ToArray();
    }

    private static void WriteBigEndian(Stream s, uint value)
    {
        s.WriteByte((byte)(value >> 24));
        s.WriteByte((byte)(value >> 16));
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)value);
    }

    private static void WriteBigEndian(Stream s, ulong value)
    {
        for (var shift = 56; shift >= 0; shift -= 8) s.WriteByte((byte)(value >> shift));
    }

    /// <summary>Equality ignores build metadata, per SemVer 2.0.0 precedence rules.</summary>
    public bool Equals(SemVer? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVer v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', PreRelease));

    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;
    public static bool operator ==(SemVer? a, SemVer? b) => a?.Equals(b) ?? b is null;
    public static bool operator !=(SemVer? a, SemVer? b) => !(a == b);
}
