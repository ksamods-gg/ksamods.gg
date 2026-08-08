using System.Text.RegularExpressions;

namespace KsaMods.Forge;

/// <summary>One release as the forge reports it, before this project has an opinion about it.</summary>
public sealed record ForgeRelease
{
    public required string Id { get; init; }
    public required string Tag { get; init; }
    public string? Name { get; init; }
    public string? Body { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public bool Draft { get; init; }
    public bool Prerelease { get; init; }
    public string? Commit { get; init; }
    public string? HtmlUrl { get; init; }
    public IReadOnlyList<ForgeAsset> Assets { get; init; } = [];
}

public sealed record ForgeAsset
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string DownloadUrl { get; init; }
    public long Size { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
}

public sealed record ForgeRepository
{
    public required string Id { get; init; }
    public required string FullName { get; init; }
    public required string DefaultBranch { get; init; }
    public bool Archived { get; init; }
    public bool Private { get; init; }
}

public sealed class ForgeException(string message, bool transient = false) : Exception(message)
{
    /// <summary>
    /// Whether retrying could plausibly work. Rate limits and 5xx are worth another attempt; a
    /// repository that does not exist is not, and burning five retries on it only delays the
    /// message that says so.
    /// </summary>
    public bool Transient { get; } = transient;
}

/// <summary>
/// What this project needs from a git forge, and nothing else (backend.md §5.6).
///
/// <para>The interface is small on purpose. The decision to support an allowlist of forges rather
/// than GitHub alone rested on four properties every mainstream forge has - an enumerable host, a
/// way to prove control of a repository, a release API and webhooks - so the abstraction is those
/// four things and no more. Anything richer would start encoding GitHub's shape and make the
/// second adapter a rewrite.</para>
/// </summary>
public interface IForge
{
    /// <summary>github, gitlab or codeberg. Matches <c>repo_link.provider</c>.</summary>
    string Provider { get; }

    Task<ForgeRepository> GetRepositoryAsync(string fullName, CancellationToken ct);

    /// <summary>Newest first. Drafts are included; deciding what to skip is the caller's job.</summary>
    Task<IReadOnlyList<ForgeRelease>> ListReleasesAsync(string fullName, CancellationToken ct);

    /// <summary>
    /// Reads the challenge file from the default branch, or null when it is not there.
    ///
    /// <para>Through the forge's API rather than a raw file URL: raw hosts serve from a CDN with
    /// its own caching, and a proof that can be answered by a stale cache is not a proof.</para>
    /// </summary>
    Task<string?> ReadVerificationFileAsync(string fullName, string path, CancellationToken ct);
}

/// <summary>
/// Which forges may be connected at all (backend.md §5.6).
///
/// <para>An allowlist rather than a URL pattern, because "somewhere that looks like a git host"
/// is not a security boundary. Adding a self-hosted instance is a moderation decision with a
/// record, not a configuration accident.</para>
/// </summary>
public static class ForgeAllowlist
{
    /// <summary>
    /// Only what has an adapter behind it. The database constraint permits gitlab and codeberg
    /// because the schema anticipates them, but accepting a provider here that nothing can talk to
    /// would let someone connect a repository the importer will never read - a listing that looks
    /// connected and imports nothing, which is worse than being told no.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ApiHosts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["github"] = "api.github.com",
        };

    public static bool Allows(string provider) => ApiHosts.ContainsKey(provider);
}

/// <summary>
/// Proving that whoever is connecting a repository can write to it (backend.md §5.2).
///
/// <para>The site issues a challenge; the claimant commits it to the default branch; the site
/// reads it back through the forge's API. Only somebody with write access can complete that, which
/// is exactly the claim being made.</para>
///
/// <para>Chosen over an OAuth scope check because it needs no stored access token and no app
/// registration, and it is identical on every forge. Its weakness is that it proves write access
/// at one moment rather than continuously; an app installation would be stronger and slots in
/// beside it - <c>repo_link.verified_by</c> records which was used.</para>
/// </summary>
public static class RepositoryProof
{
    /// <summary>Dotfile at the root: out of the way, and obviously not part of the mod.</summary>
    public const string FilePath = ".ksamods-verify";

    public static string NewChallenge() =>
        $"ksamods-verify-{Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";

    /// <summary>
    /// Whether the file proves the challenge. Trimmed because an editor will add a trailing
    /// newline and refusing over one would be a puzzle, not a safeguard; ordinal because this is a
    /// secret comparison and not a piece of text.
    /// </summary>
    public static bool Satisfies(string? fileContent, string? challenge) =>
        !string.IsNullOrWhiteSpace(challenge)
        && fileContent is not null
        && string.Equals(fileContent.Trim(), challenge.Trim(), StringComparison.Ordinal);
}

/// <summary>
/// Repository names, which arrive from a form and are about to be interpolated into a URL.
/// </summary>
public static class RepoName
{
    // Both forges use the same shape, and both are stricter than this. Being stricter here than
    // the forge only rejects things it would have accepted; being looser lets a path segment or a
    // query string through, which is the whole risk.
    private static readonly Regex Shape = new(
        @"^[A-Za-z0-9._-]{1,100}/[A-Za-z0-9._-]{1,100}$", RegexOptions.Compiled);

    public static bool IsValid(string? fullName) =>
        fullName is not null
        && Shape.IsMatch(fullName)
        // Rules out ".." as either segment, which is the traversal that would escape the API path.
        && !fullName.Contains("..", StringComparison.Ordinal);
}

/// <summary>
/// Turns a release tag into a version, or refuses.
///
/// <para>Tags are where authors are most creative and this project is least able to guess: the
/// site orders releases by SemVer and pins modlists to exact versions, so a tag it cannot parse
/// has to be skipped with a reason rather than approximated into something plausible.</para>
/// </summary>
public static class ReleaseTag
{
    public static string? ToVersion(string tag)
    {
        var candidate = tag.Trim();

        // The one convention common enough to be worth knowing. Everything past this is guessing.
        if (candidate.StartsWith('v') || candidate.StartsWith('V'))
        {
            candidate = candidate[1..];
        }

        return Metadata.SemVer.TryParse(candidate, out var version) ? version.ToString() : null;
    }
}
