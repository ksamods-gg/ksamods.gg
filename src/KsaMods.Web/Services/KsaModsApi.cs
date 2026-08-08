using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KsaMods.Web.Services;

/// <summary>
/// Typed client for the ksamods.gg API.
///
/// <para>Server-side calls forward the caller's session cookie from the incoming request, so a
/// server-rendered page sees exactly what that user is allowed to see - a maintainer gets their
/// failed releases, a stranger does not. Rendering a page as though nobody were signed in and
/// then correcting it on the client is how you leak a flash of the wrong content.</para>
/// </summary>
public sealed class KsaModsApi(IHttpClientFactory factory, IHttpContextAccessor accessor)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private HttpClient CreateClient()
    {
        var client = factory.CreateClient(ApiProxy.ClientName);

        if (accessor.HttpContext?.Request.Headers.Cookie is { Count: > 0 } cookie)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie.ToArray());
        }

        return client;
    }

    public Task<ModSummaryPage?> SearchAsync(
        string? query = null, string? tag = null, int? gameBuild = null,
        string? after = null, int limit = 24, CancellationToken ct = default)
    {
        var parameters = new List<string> { $"limit={limit}" };
        if (!string.IsNullOrWhiteSpace(query)) parameters.Add($"q={Uri.EscapeDataString(query)}");
        if (!string.IsNullOrWhiteSpace(tag)) parameters.Add($"tag={Uri.EscapeDataString(tag)}");
        if (gameBuild is not null) parameters.Add($"gameBuild={gameBuild}");
        if (!string.IsNullOrWhiteSpace(after)) parameters.Add($"after={Uri.EscapeDataString(after)}");

        return GetAsync<ModSummaryPage>($"/api/v1/mods?{string.Join('&', parameters)}", ct);
    }

    public Task<ModDetail?> GetModAsync(string id, CancellationToken ct = default) =>
        GetAsync<ModDetail>($"/api/v1/mods/{Uri.EscapeDataString(id)}", ct);

    public Task<ReleaseDetail?> GetReleaseAsync(string id, string version, CancellationToken ct = default) =>
        GetAsync<ReleaseDetail>(
            $"/api/v1/mods/{Uri.EscapeDataString(id)}/releases/{Uri.EscapeDataString(version)}", ct);

    public Task<CollisionResult?> GetCollisionsAsync(string assetId, CancellationToken ct = default) =>
        GetAsync<CollisionResult>($"/api/v1/collisions/{Uri.EscapeDataString(assetId)}", ct);

    public Task<IReadOnlyList<GameBuild>?> GetBuildsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<GameBuild>>("/api/v1/builds", ct);

    /// <summary>
    /// The signed-in account, or null for an anonymous visitor.
    ///
    /// <para>The session cookie travels with this because <see cref="CreateClient"/> forwards it,
    /// so a server-rendered page gets the right answer on first paint rather than after a round
    /// trip. Null covers both "no session" and "the API did not answer": a page guarding a form
    /// should send someone to sign in either way rather than let them fill it in hopefully.</para>
    /// </summary>
    public async Task<CurrentAccount?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/api/v1/me", ct);

            // 204 is the documented anonymous answer; anything unsuccessful is treated the same.
            if (response.StatusCode == HttpStatusCode.NoContent || !response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CurrentAccount>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    // ── account management ──

    public Task<AccountProfile?> GetProfileAsync(CancellationToken ct = default) =>
        GetAsync<AccountProfile>("/api/v1/me/profile", ct);

    public Task<IReadOnlyList<AccountSession>?> GetSessionsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AccountSession>>("/api/v1/me/sessions", ct);

    public async Task<ApiOutcome> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PatchAsJsonAsync("/api/v1/me/profile", request, Json, ct);

        return response.IsSuccessStatusCode
            ? ApiOutcome.Ok()
            : ApiOutcome.Failed(await ReadProblemAsync(response, ct), response.StatusCode);
    }

    public async Task<int> RevokeOtherSessionsAsync(CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync("/api/v1/me/sessions/revoke-others", null, ct);

        if (!response.IsSuccessStatusCode) return 0;

        var body = await response.Content.ReadFromJsonAsync<RevokedResponse>(Json, ct);
        return body?.Revoked ?? 0;
    }

    public async Task<ApiOutcome> UnlinkIdentityAsync(string provider, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.DeleteAsync(
            $"/api/v1/me/identities/{Uri.EscapeDataString(provider)}", ct);

        return response.IsSuccessStatusCode
            ? ApiOutcome.Ok()
            : ApiOutcome.Failed(await ReadProblemAsync(response, ct), response.StatusCode);
    }

    public async Task<ApiOutcome> DeleteAccountAsync(CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync("/api/v1/me/delete", null, ct);

        return response.IsSuccessStatusCode
            ? ApiOutcome.Ok()
            : ApiOutcome.Failed(await ReadProblemAsync(response, ct), response.StatusCode);
    }

    public async Task<CreateModResult> CreateModAsync(CreateModRequest request, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/mods", request, Json, ct);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateModResponse>(Json, ct);
            return new CreateModResult(true, created?.Id, created?.Note, null);
        }

        // The API distinguishes a taken id from a malformed one, and the difference is the whole
        // message: one means "pick another name", the other means "that name cannot exist".
        //
        // The status comes back too. Not every rejection has a body worth reading: a 401 is a
        // bare ProblemDetails with no detail field, and without the status the form can only say
        // something vague when the real answer is "you are not signed in".
        var problem = await ReadProblemAsync(response, ct);
        return new CreateModResult(false, null, null, problem, response.StatusCode);
    }

    public async Task<CreateModResult> EditModAsync(
        string modId, EditModRequest request, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}", request, Json, ct);

        return response.IsSuccessStatusCode
            ? new CreateModResult(true, modId, null, null, response.StatusCode)
            : new CreateModResult(false, null, null, await ReadProblemAsync(response, ct), response.StatusCode);
    }

    /// <summary>Publishes or unlists. <paramref name="listed"/> false means "take it back out of browse".</summary>
    public async Task<CreateModResult> SetModVisibilityAsync(
        string modId, bool listed, CancellationToken ct = default)
    {
        var action = listed ? "publish" : "unlist";

        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}/{action}", content: null, ct);

        return response.IsSuccessStatusCode
            ? new CreateModResult(true, modId, null, null, response.StatusCode)
            : new CreateModResult(false, null, null, await ReadProblemAsync(response, ct), response.StatusCode);
    }

    /// <summary>
    /// Deletes a listing. Fails with a 409 and a readable reason when something still points at
    /// it, which is the normal answer for anything that has ever published a release.
    /// </summary>
    public async Task<CreateModResult> DeleteModAsync(string modId, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.DeleteAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}", ct);

        return response.IsSuccessStatusCode
            ? new CreateModResult(true, modId, null, null, response.StatusCode)
            : new CreateModResult(false, null, null, await ReadProblemAsync(response, ct), response.StatusCode);
    }

    public async Task<bool> ConnectRepositoryAsync(
        string modId, ConnectRepoRequest request, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}/repo-link", request, Json, ct);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ImportReleasesAsync(string modId, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}/releases/import", content: null, ct);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> YankAsync(string modId, string version, string reason, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}/releases/{Uri.EscapeDataString(version)}/yank",
            new { reason }, Json, ct);

        return response.IsSuccessStatusCode;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);

        // A missing listing is an ordinary outcome for a URL a user typed, not an exception.
        if (response.StatusCode == HttpStatusCode.NotFound) return default;

        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(response.StatusCode, await ReadProblemAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private static async Task<string?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return null;

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("detail", out var detail)) return detail.GetString();
            if (root.TryGetProperty("error", out var error)) return error.GetString();

            // ValidationProblemDetails puts the useful text one level down, under the field name.
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                    {
                        return field.Value[0].GetString();
                    }
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// ── responses ──

public sealed record CurrentAccount
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("handle")] public string Handle { get; init; } = "";
    [JsonPropertyName("site_role")] public string SiteRole { get; init; } = "user";
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }

    public bool IsModerator => SiteRole is "moderator" or "admin";
}

public sealed record ModSummaryPage
{
    [JsonPropertyName("items")] public IReadOnlyList<ModSummary> Items { get; init; } = [];
    [JsonPropertyName("next")] public string? Next { get; init; }
}

public sealed record ModSummary
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "mod";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("abstract")] public string Abstract { get; init; } = "";
    [JsonPropertyName("tags")] public IReadOnlyList<string> Tags { get; init; } = [];
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("banner_url")] public string? BannerUrl { get; init; }
}

public sealed record ModDetail
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "mod";
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("abstract")] public string? Abstract { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("license")] public string? License { get; init; }
    [JsonPropertyName("tags")] public IReadOnlyList<string> Tags { get; init; } = [];
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("superseded_by")] public string? SupersededBy { get; init; }
    [JsonPropertyName("listing_state")] public string ListingState { get; init; } = "listed";
    [JsonPropertyName("banner_url")] public string? BannerUrl { get; init; }

    /// <summary>owner, maintainer, or null for everyone else. Decides who sees the manage controls.</summary>
    [JsonPropertyName("your_role")] public string? YourRole { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
    [JsonPropertyName("releases")] public IReadOnlyList<ReleaseSummary> Releases { get; init; } = [];

    /// <summary>Set when the API served a withdrawal notice in place of the listing.</summary>
    [JsonPropertyName("message")] public string? Message { get; init; }

    public bool IsWithdrawn => ListingState is "delisted" or "taken_down";
    public bool IsDeprecated => Status == "deprecated";

    /// <summary>Not in browse or search. Still reachable by anyone with the link.</summary>
    public bool IsDraft => ListingState == "unlisted";

    public bool IsOwner => YourRole == "owner";
    public bool CanManage => YourRole is "owner" or "maintainer";
}

public sealed record ReleaseSummary
{
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("release_status")] public string ReleaseStatus { get; init; } = "stable";
    [JsonPropertyName("released_at")] public DateTimeOffset ReleasedAt { get; init; }
    [JsonPropertyName("game_min_revision")] public int? GameMinRevision { get; init; }
    [JsonPropertyName("game_max_revision")] public int? GameMaxRevision { get; init; }
    [JsonPropertyName("availability")] public string Availability { get; init; } = "unverified";
    [JsonPropertyName("validation_state")] public string ValidationState { get; init; } = "pending";
    [JsonPropertyName("yanked")] public bool Yanked { get; init; }
}

public sealed record ReleaseDetail
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("release_status")] public string ReleaseStatus { get; init; } = "stable";
    [JsonPropertyName("released_at")] public DateTimeOffset ReleasedAt { get; init; }
    [JsonPropertyName("game_min")] public string? GameMin { get; init; }
    [JsonPropertyName("game_min_revision")] public int? GameMinRevision { get; init; }
    [JsonPropertyName("game_max")] public string? GameMax { get; init; }
    [JsonPropertyName("game_max_revision")] public int? GameMaxRevision { get; init; }
    [JsonPropertyName("install_size")] public long? InstallSize { get; init; }
    [JsonPropertyName("availability")] public string Availability { get; init; } = "unverified";
    [JsonPropertyName("last_verified_at")] public DateTimeOffset? LastVerifiedAt { get; init; }
    [JsonPropertyName("validation_state")] public string ValidationState { get; init; } = "pending";
    [JsonPropertyName("yanked")] public bool Yanked { get; init; }
    [JsonPropertyName("yanked_reason")] public string? YankedReason { get; init; }
    [JsonPropertyName("changelog")] public string? Changelog { get; init; }
    [JsonPropertyName("download")] public DownloadInfo? Download { get; init; }
    [JsonPropertyName("mirrors")] public IReadOnlyList<string> Mirrors { get; init; } = [];
    [JsonPropertyName("loader")] public LoaderInfo? Loader { get; init; }
    [JsonPropertyName("dependencies")] public IReadOnlyList<DependencyInfo> Dependencies { get; init; } = [];
    [JsonPropertyName("console")] public IReadOnlyList<ConsoleCommandInfo> Console { get; init; } = [];
    [JsonPropertyName("findings")] public IReadOnlyList<FindingInfo> Findings { get; init; } = [];
}

public sealed record DownloadInfo
{
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("content_type")] public string ContentType { get; init; } = "application/zip";
}

public sealed record LoaderInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("min")] public string? Min { get; init; }
    [JsonPropertyName("max")] public string? Max { get; init; }
}

public sealed record DependencyInfo
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("kind")] public string Kind { get; init; } = "required";
    [JsonPropertyName("min")] public string? Min { get; init; }
    [JsonPropertyName("max")] public string? Max { get; init; }
    [JsonPropertyName("source")] public string Source { get; init; } = "derived";
}

public sealed record ConsoleCommandInfo
{
    [JsonPropertyName("hook")] public string Hook { get; init; } = "";
    [JsonPropertyName("ordinal")] public int Ordinal { get; init; }
    [JsonPropertyName("command")] public string Command { get; init; } = "";
}

public sealed record FindingInfo
{
    [JsonPropertyName("stage")] public int Stage { get; init; }
    [JsonPropertyName("severity")] public string Severity { get; init; } = "info";
    [JsonPropertyName("code")] public string Code { get; init; } = "";
    [JsonPropertyName("message")] public string Message { get; init; } = "";
    [JsonPropertyName("path")] public string? Path { get; init; }
}

public sealed record CollisionResult
{
    [JsonPropertyName("asset_id")] public string AssetId { get; init; } = "";
    [JsonPropertyName("declared_by")] public IReadOnlyList<CollisionOwner> DeclaredBy { get; init; } = [];
    [JsonPropertyName("note")] public string? Note { get; init; }
}

public sealed record CollisionOwner
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("xml_path")] public string XmlPath { get; init; } = "";
}

/// <summary>Outcome of a write, carrying the API's own explanation rather than a generic one.</summary>
public sealed record ApiOutcome(bool Success, string? Error, HttpStatusCode? Status)
{
    public static ApiOutcome Ok() => new(true, null, null);
    public static ApiOutcome Failed(string? error, HttpStatusCode status) => new(false, error, status);

    /// <summary>True when the session expired mid-page - the caller should send them to sign in.</summary>
    public bool NeedsSignIn => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

public sealed record AccountProfile
{
    [JsonPropertyName("handle")] public string Handle { get; init; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("forums_url")] public string? ForumsUrl { get; init; }
    [JsonPropertyName("site_role")] public string SiteRole { get; init; } = "user";
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("identities")] public IReadOnlyList<LinkedIdentity> Identities { get; init; } = [];
    [JsonPropertyName("mods")] public IReadOnlyList<OwnedMod> Mods { get; init; } = [];
    [JsonPropertyName("modlists")] public IReadOnlyList<OwnedModlist> Modlists { get; init; } = [];

    public bool IsModerator => SiteRole is "moderator" or "admin";
}

public sealed record LinkedIdentity
{
    [JsonPropertyName("provider")] public string Provider { get; init; } = "";
    [JsonPropertyName("linked_at")] public DateTimeOffset LinkedAt { get; init; }
}

public sealed record OwnedMod
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("role")] public string Role { get; init; } = "maintainer";
}

public sealed record OwnedModlist
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("role")] public string Role { get; init; } = "editor";
    [JsonPropertyName("visibility")] public string Visibility { get; init; } = "private";
}

public sealed record AccountSession
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("issued_at")] public DateTimeOffset IssuedAt { get; init; }
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; init; }
    [JsonPropertyName("user_agent")] public string? UserAgent { get; init; }
    [JsonPropertyName("current")] public bool Current { get; init; }
}

public sealed record UpdateProfileRequest(string? DisplayName, string? Handle, string? ForumsUrl);

internal sealed record RevokedResponse
{
    [JsonPropertyName("revoked")] public int Revoked { get; init; }
}

public sealed record GameBuild
{
    [JsonPropertyName("revision")] public int Revision { get; init; }
    [JsonPropertyName("build")] public string Build { get; init; } = "";
    [JsonPropertyName("date")] public DateTime? Date { get; init; }
}

// ── requests ──

public sealed record CreateModRequest(
    string Id, string Name, string Abstract, string License,
    string? Description = null, string[]? Tags = null,
    Dictionary<string, string>? Links = null, string? BannerUrl = null);

/// <summary>Null means "leave this one alone", so a partial edit stays partial.</summary>
public sealed record EditModRequest(
    string? Name = null, string? Abstract = null, string? Description = null,
    string? License = null, string[]? Tags = null,
    Dictionary<string, string>? Links = null, string? BannerUrl = null);

public sealed record ConnectRepoRequest(
    string Provider, string RepoId, string RepoFullName,
    string? InstallationId = null, string? AssetGlob = null);

public sealed record CreateModResponse
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("note")] public string? Note { get; init; }
}

public sealed record CreateModResult(
    bool Success, string? Id, string? Note, string? Error, HttpStatusCode? Status = null)
{
    /// <summary>No session, or a session without permission. The fix is signing in, not editing the form.</summary>
    public bool NeedsSignIn => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
