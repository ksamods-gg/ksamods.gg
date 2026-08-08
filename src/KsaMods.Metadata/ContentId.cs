using System.Diagnostics.CodeAnalysis;

namespace KsaMods.Metadata;

/// <summary>
/// A content id: the KSA folder name for a mod, and the identity for every other content type.
///
/// Rules are RFC 0031's, adopted verbatim by spec §3. They are deliberately stricter than any
/// single filesystem because the id must be a valid folder name on Windows, Linux and macOS at
/// once, and because <c>ModManifest.Save</c> writes it into manifest.toml unescaped.
/// </summary>
public readonly struct ContentId : IEquatable<ContentId>
{
    public const int MinLength = 1;
    public const int MaxLength = 64;

    /// <summary>The id as its author wrote it. Display and on-disk use this.</summary>
    public string Value { get; }

    /// <summary>Lowercased form. Every comparison and lookup uses this (RFC 0031: ids compare case-insensitively).</summary>
    public string Lower { get; }

    private ContentId(string value)
    {
        Value = value;
        Lower = value.ToLowerInvariant();
    }

    /// <summary>
    /// Reserved names, compared case-insensitively against the id <b>up to its first dot</b>.
    /// The truncation matters: Windows treats dotted forms such as <c>CON.mod</c> as devices too,
    /// so checking the whole string misses them.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        // The game's own mod: it ships Content/Core and ModLibrary.PrepareManifest exempts
        // that manifest entry from cleanup.
        "Core",
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool TryParse(string? value, [NotNullWhen(true)] out ContentId? id, out IdRejection reason)
    {
        id = null;

        if (string.IsNullOrEmpty(value))
        {
            reason = IdRejection.Empty;
            return false;
        }

        if (value.Length is < MinLength or > MaxLength)
        {
            // 64 characters, because the id lands inside real paths under the user's Documents
            // folder and Windows caps a path at 260 characters unless long paths are enabled.
            reason = IdRejection.Length;
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var isAlphanumeric = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

            if (i == 0 || i == value.Length - 1)
            {
                // No leading dot, because that hides the folder on Unix. No trailing dot or
                // space, because Windows silently strips or rejects them.
                if (!isAlphanumeric)
                {
                    reason = IdRejection.Boundary;
                    return false;
                }
                continue;
            }

            // ASCII only, because macOS normalises non-ASCII folder names differently from other
            // platforms, so one id could be two distinct byte sequences on two machines.
            if (!isAlphanumeric && c is not ('.' or '-' or '_'))
            {
                reason = IdRejection.Charset;
                return false;
            }
        }

        if (IsReserved(value))
        {
            reason = IdRejection.Reserved;
            return false;
        }

        id = new ContentId(value);
        reason = IdRejection.None;
        return true;
    }

    public static bool IsReserved(string value)
    {
        var dot = value.IndexOf('.');
        var head = dot < 0 ? value : value[..dot];
        return Reserved.Contains(head);
    }

    public static ContentId Parse(string value)
    {
        if (!TryParse(value, out var id, out var reason))
        {
            throw new FormatException($"'{value}' is not a valid content id: {reason}.");
        }
        return id.Value;
    }

    /// <summary>
    /// Case-insensitive, because folder names are case-insensitive on Windows and case-sensitive
    /// on Linux, so MyMod and mymod would be one mod on one machine and two on another.
    /// </summary>
    public bool Equals(ContentId other) => string.Equals(Lower, other.Lower, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ContentId other && Equals(other);
    public override int GetHashCode() => Lower.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Value;

    public static bool operator ==(ContentId a, ContentId b) => a.Equals(b);
    public static bool operator !=(ContentId a, ContentId b) => !a.Equals(b);
}

public enum IdRejection
{
    None,
    Empty,
    Length,
    /// <summary>First or last character is not a letter or digit.</summary>
    Boundary,
    /// <summary>Contains something outside [A-Za-z0-9._-].</summary>
    Charset,
    /// <summary>Core, or a Windows device name, up to the first dot.</summary>
    Reserved,
}
