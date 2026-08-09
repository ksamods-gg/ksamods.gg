using System.Net;
using System.Text;
using System.Text.Json;

namespace KsaMods.Forge;

/// <summary>
/// GitHub, over the public REST API.
///
/// <para>Unauthenticated by default, which is a real constraint rather than an oversight: 60
/// requests an hour per address. Set a token and it becomes 5000. The site reads public
/// repositories only, so the token buys rate limit and nothing else - it is never required to see
/// anything, and a missing one degrades to slower rather than broken.</para>
/// </summary>
public sealed class GitHubForge(HttpClient client, string? token = null) : IForge
{
    public string Provider => "github";

    /// <summary>Enough for any real project; a repository with more has a different problem.</summary>
    private const int MaxReleases = 100;

    public async Task<ForgeRepository> GetRepositoryAsync(string fullName, CancellationToken ct)
    {
        using var document = await GetAsync($"repos/{fullName}", fullName, ct)
            ?? throw new ForgeException($"No repository at github.com/{fullName}, or it is private.");

        var root = document.RootElement;

        return new ForgeRepository
        {
            Id = root.GetProperty("id").GetRawText(),
            FullName = root.GetProperty("full_name").GetString() ?? fullName,
            DefaultBranch = root.TryGetProperty("default_branch", out var branch)
                ? branch.GetString() ?? "main"
                : "main",
            Archived = root.TryGetProperty("archived", out var archived) && archived.GetBoolean(),
            Private = root.TryGetProperty("private", out var isPrivate) && isPrivate.GetBoolean(),

            // Present on this response already, so proving ownership by topic costs no extra
            // request. Absent on older API versions rather than empty, hence the TryGetProperty.
            Topics = root.TryGetProperty("topics", out var topics) && topics.ValueKind == JsonValueKind.Array
                ? [.. topics.EnumerateArray()
                        .Select(t => t.GetString())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t!)]
                : [],
        };
    }

    public async Task<IReadOnlyList<ForgeRelease>> ListReleasesAsync(string fullName, CancellationToken ct)
    {
        using var document = await GetAsync($"repos/{fullName}/releases?per_page={MaxReleases}", fullName, ct)
            ?? throw new ForgeException($"No repository at github.com/{fullName}, or it is private.");

        var releases = new List<ForgeRelease>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var assets = new List<ForgeAsset>();

            if (element.TryGetProperty("assets", out var assetArray))
            {
                foreach (var asset in assetArray.EnumerateArray())
                {
                    assets.Add(new ForgeAsset
                    {
                        Id = asset.GetProperty("id").GetRawText(),
                        Name = asset.GetProperty("name").GetString() ?? "",
                        // browser_download_url, not the API url: the API one answers with JSON
                        // unless you ask for an octet-stream, and the site records the address a
                        // person could paste into a browser.
                        DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "",
                        Size = asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                        ContentType = asset.TryGetProperty("content_type", out var type)
                            ? type.GetString() ?? "application/octet-stream"
                            : "application/octet-stream",
                    });
                }
            }

            releases.Add(new ForgeRelease
            {
                Id = element.GetProperty("id").GetRawText(),
                Tag = element.GetProperty("tag_name").GetString() ?? "",
                Name = element.TryGetProperty("name", out var name) ? name.GetString() : null,
                Body = element.TryGetProperty("body", out var body) ? body.GetString() : null,
                PublishedAt = element.TryGetProperty("published_at", out var published)
                              && published.ValueKind is JsonValueKind.String
                    ? published.GetDateTimeOffset()
                    : DateTimeOffset.UnixEpoch,
                Draft = element.TryGetProperty("draft", out var draft) && draft.GetBoolean(),
                Prerelease = element.TryGetProperty("prerelease", out var pre) && pre.GetBoolean(),
                Commit = element.TryGetProperty("target_commitish", out var commit)
                    ? commit.GetString()
                    : null,
                HtmlUrl = element.TryGetProperty("html_url", out var html) ? html.GetString() : null,
                Assets = assets,
            });
        }

        return releases;
    }

    public async Task<string?> ReadVerificationFileAsync(string fullName, string path, CancellationToken ct)
    {
        using var document = await GetAsync($"repos/{fullName}/contents/{path}", fullName, ct);

        if (document is null) return null;

        var root = document.RootElement;

        // A directory comes back as an array. Somebody creating a directory with this name has
        // not proven anything.
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("content", out var content)) return null;

        var encoded = content.GetString();
        if (string.IsNullOrWhiteSpace(encoded)) return null;

        try
        {
            // GitHub wraps base64 at 60 characters, so the newlines have to go before decoding.
            return Encoding.UTF8.GetString(
                Convert.FromBase64String(encoded.Replace("\n", "").Replace("\r", "")));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// One request. Returns null for 404 - "not there" is an answer this caller acts on, not an
    /// error - and throws for everything else, marking what is worth retrying.
    /// </summary>
    private async Task<JsonDocument?> GetAsync(string path, string fullName, CancellationToken ct)
    {
        if (!RepoName.IsValid(fullName))
        {
            throw new ForgeException($"'{fullName}' is not a repository name.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.TryAddWithoutValidation("User-Agent", "ksamods.gg");

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        using var response = await client.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        if (!response.IsSuccessStatusCode)
        {
            // 403 with the rate limit exhausted is the common failure of an unauthenticated
            // client, and it is temporary. Saying so is the difference between a job that
            // succeeds in an hour and one that is declared dead.
            var rateLimited = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
                && remaining.FirstOrDefault() == "0";

            var transient = rateLimited || (int)response.StatusCode >= 500;

            throw new ForgeException(
                rateLimited
                    ? "GitHub's rate limit is exhausted. Set GitHub__Token to raise it from 60 requests an hour to 5000."
                    : $"GitHub answered {(int)response.StatusCode} for {path}.",
                transient);
        }

        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }
}
