namespace KsaMods.Web.Services;

public sealed record Pill(string Label, string Variant, string Explanation);

/// <summary>
/// Turns API state strings into what the user actually sees.
///
/// <para>Pure and separate from the components so it can be tested, because this is where the
/// site either tells the truth or quietly overstates. "Verified 3 days ago" and "the bytes
/// changed since we checked" are one enum apart, and a mislabelled pill is a lie the user has no
/// way to detect.</para>
/// </summary>
public static class Presentation
{
    /// <summary>
    /// RFC 0017's four compatibility states. Only Incompatible is a refusal — the copy has to
    /// carry that, or a warning reads as a block and users stop trusting either.
    /// </summary>
    public static Pill Compatibility(int? installedRevision, int? min, int? max)
    {
        if (min is null)
        {
            return new Pill("Unknown", "outline",
                "This release declares no minimum game build, so compatibility cannot be evaluated.");
        }

        if (installedRevision is null)
        {
            return new Pill($"Needs {min}+", "outline",
                "Tell us your game build to see whether this release is compatible.");
        }

        if (installedRevision < min)
        {
            return new Pill("Incompatible", "error",
                $"Needs game revision {min} or newer; you are on {installedRevision}.");
        }

        if (max is not null && installedRevision > max)
        {
            return new Pill("Untested", "warning",
                $"Tested up to revision {max}; you are on {installedRevision}. It may still work.");
        }

        return new Pill("Compatible", "ok", $"Tested against your game build ({installedRevision}).");
    }

    /// <summary>
    /// Artifact availability. The site never stores the file, so every one of these is a statement
    /// about somebody else's server — the wording says so rather than implying the site holds it.
    /// </summary>
    public static Pill Availability(string availability, DateTimeOffset? lastVerified) => availability switch
    {
        "verified" => new Pill(
            lastVerified is null ? "Verified" : $"Verified {Ago(lastVerified.Value)}",
            "ok",
            "The file at this link still matches the hash recorded when it was imported."),

        "unavailable" => new Pill("Download gone", "error",
            "The link no longer resolves. The record is kept because modlists may still pin this version."),

        "diverged" => new Pill("Bytes changed", "warning",
            "The file at this link differs from the one imported, but looks like a re-pack rather than a "
            + "material change. The recorded hash is unchanged; ask the author to cut a new version."),

        "quarantined" => new Pill("Quarantined", "error",
            "The file changed materially since import — different assemblies, console commands or asset ids. "
            + "The download is hidden pending review."),

        _ => new Pill("Not yet verified", "outline",
            "This release has not been re-checked since it was imported."),
    };

    public static Pill Validation(string state) => state switch
    {
        "passed" => new Pill("Passed", "ok", "Validation found no problems."),
        "passed_warnings" => new Pill("Passed with warnings", "warning",
            "Installable, but validation found things worth fixing."),
        "failed" => new Pill("Failed", "error",
            "Validation found errors. This release is visible only to its maintainers until they are fixed."),
        "running" => new Pill("Validating", "info", "The archive is being checked now."),
        _ => new Pill("Queued", "outline", "Waiting to be validated."),
    };

    public static string SeverityVariant(string severity) => severity switch
    {
        "error" => "error",
        "warning" => "warning",
        _ => "info",
    };

    /// <summary>
    /// Human name for a pipeline stage, so a finding says where it came from without the reader
    /// needing the spec open.
    /// </summary>
    public static string StageName(int stage) => stage switch
    {
        1 => "Fetch",
        2 => "Integrity",
        3 => "Archive safety",
        4 => "Structure",
        5 => "Declared content",
        6 => "Asset ids",
        7 => "Code",
        8 => "Risk surface",
        _ => $"Stage {stage}",
    };

    public static string ReleaseChannel(string status) => status switch
    {
        "testing" => "Beta",
        "dev" => "Dev",
        _ => "Stable",
    };

    public static string Bytes(long? bytes)
    {
        if (bytes is null or < 0) return "—";

        string[] units = ["B", "KiB", "MiB", "GiB"];
        double value = bytes.Value;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    public static string Ago(DateTimeOffset when, DateTimeOffset? now = null)
    {
        var elapsed = (now ?? DateTimeOffset.UtcNow) - when;

        if (elapsed < TimeSpan.Zero) return "just now";
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays}d ago";
        if (elapsed.TotalDays < 365) return $"{(int)(elapsed.TotalDays / 30)}mo ago";

        return $"{(int)(elapsed.TotalDays / 365)}y ago";
    }

    /// <summary>Abbreviates a hash for display while keeping enough to eyeball a comparison.</summary>
    public static string ShortHash(string sha256) =>
        sha256.Length <= 16 ? sha256 : $"{sha256[..8]}…{sha256[^8..]}";
}
