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

    /// <summary>
    /// <paramref name="type"/> is <c>mod</c> or <c>mod-loader</c>; null returns both. The API has
    /// always taken it and nothing asked, so loaders sat in the catalogue among the things they
    /// exist to load.
    /// </summary>
    public Task<ModSummaryPage?> SearchAsync(
        string? query = null, string? tag = null, int? gameBuild = null,
        string? after = null, int limit = 24, string? type = null, CancellationToken ct = default)
    {
        var parameters = new List<string> { $"limit={limit}" };
        if (!string.IsNullOrWhiteSpace(query)) parameters.Add($"q={Uri.EscapeDataString(query)}");
        if (!string.IsNullOrWhiteSpace(tag)) parameters.Add($"tag={Uri.EscapeDataString(tag)}");
        if (!string.IsNullOrWhiteSpace(type)) parameters.Add($"type={Uri.EscapeDataString(type)}");
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

    /// <summary>
    /// Everything the caller maintains, with enough state to decide what to do about each one.
    /// Richer than the list on the profile, which answers "what do I have" rather than "what
    /// needs me".
    /// </summary>
    public Task<IReadOnlyList<OwnedModStatus>?> GetMyModsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<OwnedModStatus>>("/api/v1/me/mods", ct);

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

    // ── tags ──

    /// <summary>
    /// The site's curated vocabulary. RFC 0031 leaves tags free-form and notes a vocabulary can
    /// come later without a format change; this is that list, so the picker offers it rather than
    /// letting somebody invent a synonym for a tag that already exists.
    /// </summary>
    public Task<TagListPage?> GetTagsAsync(CancellationToken ct = default) =>
        GetAsync<TagListPage>("/api/v1/tags", ct);

    public async Task<TagProposalResult> ProposeTagAsync(
        string slug, string? reason, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/tags", new { slug, reason }, Json, ct);

        if (!response.IsSuccessStatusCode)
        {
            return new TagProposalResult(false, null, await ReadProblemAsync(response, ct));
        }

        var body = await response.Content.ReadFromJsonAsync<TagProposalResponse>(Json, ct);

        return new TagProposalResult(true, body, null);
    }

    public Task<IReadOnlyList<AdminTag>?> GetAdminTagsAsync(
        string? state = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AdminTag>>($"/api/v1/admin/tags{Query(("state", state))}", ct);

    public Task<IReadOnlyList<UnknownTag>?> GetUnknownTagsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<UnknownTag>>("/api/v1/admin/tags/unknown", ct);

    public Task<ApiOutcome> ApproveTagAsync(
        string slug, string? label, string? description, CancellationToken ct = default) =>
        PostAsync($"/api/v1/admin/tags/{Uri.EscapeDataString(slug)}/approve",
            new { label, description }, ct);

    public Task<ApiOutcome> RejectTagAsync(string slug, string note, CancellationToken ct = default) =>
        PostAsync($"/api/v1/admin/tags/{Uri.EscapeDataString(slug)}/reject", new { note }, ct);

    public Task<ApiOutcome> CreateTagAsync(
        string slug, string? label, string? description, CancellationToken ct = default) =>
        PostAsync("/api/v1/admin/tags", new { slug, label, description }, ct);

    // ── moderation ──
    //
    // Every read here comes back null when the caller is not staff, because the API answers 404
    // rather than 403 - /admin does not confirm to a stranger that it exists. The pages treat null
    // as "there is nothing here for you", which is the same thing.

    public Task<AdminOverview?> GetAdminOverviewAsync(CancellationToken ct = default) =>
        GetAsync<AdminOverview>("/api/v1/admin/overview", ct);

    public Task<IReadOnlyList<AdminReport>?> GetReportsAsync(string? state = "open", CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AdminReport>>(
            string.IsNullOrEmpty(state) ? "/api/v1/admin/reports" : $"/api/v1/admin/reports?state={state}", ct);

    public Task<ApiOutcome> ResolveReportAsync(
        long id, string state, string rationale, CancellationToken ct = default) =>
        PostAsync($"/api/v1/admin/reports/{id}/resolve", new { state, rationale }, ct);

    public Task<IReadOnlyList<AdminReview>?> GetReviewQueueAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AdminReview>>("/api/v1/admin/reviews", ct);

    public Task<ApiOutcome> ClearReviewAsync(long releaseId, string? notes, CancellationToken ct = default) =>
        PostAsync($"/api/v1/admin/reviews/{releaseId}/clear", new { notes }, ct);

    public Task<IReadOnlyList<AdminListing>?> GetAdminListingsAsync(
        string? q = null, string? state = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AdminListing>>($"/api/v1/admin/listings{Query(("q", q), ("state", state))}", ct);

    public Task<ApiOutcome> SetListingStateAsync(
        string modId, string state, string rationale, CancellationToken ct = default) =>
        PostAsync($"/api/v1/admin/listings/{Uri.EscapeDataString(modId)}/state",
            new { state, rationale }, ct);

    public Task<IReadOnlyList<AdminAccount>?> GetAdminAccountsAsync(
        string? q = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AdminAccount>>($"/api/v1/admin/accounts{Query(("q", q))}", ct);

    public Task<ApiOutcome> SuspendAccountAsync(
        long id, bool suspended, string rationale, CancellationToken ct = default) =>
        PostAsync($"/api/v1/admin/accounts/{id}/suspend", new { suspended, rationale }, ct);

    public Task<ApiOutcome> SetSiteRoleAsync(
        long id, string role, string rationale, CancellationToken ct = default) =>
        PostAsync($"/api/v1/admin/accounts/{id}/role", new { role, rationale }, ct);

    public Task<IReadOnlyList<ModerationEntry>?> GetModerationLogAsync(
        string? subjectId = null, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ModerationEntry>>($"/api/v1/admin/log{Query(("subjectId", subjectId))}", ct);

    public Task<IReadOnlyList<AdminJob>?> GetJobsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AdminJob>>("/api/v1/admin/jobs", ct);

    public Task<ApiOutcome> RetryJobAsync(long id, CancellationToken ct = default) =>
        PostAsync($"/api/v1/admin/jobs/{id}/retry", new { }, ct);

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

    /// <summary>
    /// Claims a repository. This does not connect it: the answer carries a challenge to publish in
    /// the repository, which <see cref="VerifyRepositoryAsync"/> then reads back.
    /// </summary>
    public async Task<RepoLinkResult> ConnectRepositoryAsync(
        string modId, ConnectRepoRequest request, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}/repo-link", request, Json, ct);

        if (!response.IsSuccessStatusCode)
        {
            return new RepoLinkResult(false, null, await ReadProblemAsync(response, ct));
        }

        return new RepoLinkResult(
            true, await response.Content.ReadFromJsonAsync<RepoLinkChallenge>(Json, ct), null);
    }

    public async Task<RepoLinkResult> VerifyRepositoryAsync(string modId, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}/repo-link/verify", content: null, ct);

        if (!response.IsSuccessStatusCode)
        {
            return new RepoLinkResult(false, null, await ReadProblemAsync(response, ct));
        }

        return new RepoLinkResult(true, null, null);
    }

    public async Task<ApiOutcome> ImportReleasesAsync(string modId, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}/releases/import", content: null, ct);

        return response.IsSuccessStatusCode
            ? ApiOutcome.Ok()
            : ApiOutcome.Failed(await ReadProblemAsync(response, ct), response.StatusCode);
    }

    public async Task<bool> YankAsync(string modId, string version, string reason, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}/releases/{Uri.EscapeDataString(version)}/yank",
            new { reason }, Json, ct);

        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// The connected repository and, while it is still unproven, the challenge to publish.
    ///
    /// <para>Lets the manage page show the verification instructions on load rather than only in
    /// the response to the request that connected the repository. Null when nothing is connected
    /// yet, which is the ordinary case for a new listing.</para>
    /// </summary>
    public Task<RepoLinkChallenge?> GetRepoLinkAsync(string modId, CancellationToken ct = default) =>
        GetAsync<RepoLinkChallenge>($"/api/v1/mods/{Uri.EscapeDataString(modId)}/repo-link", ct);

    /// <summary>
    /// Files a report against a mod, release, modlist or account.
    ///
    /// <para>Needs a session: a report is attributable so a person who files nonsense repeatedly
    /// can be stopped. Filing the same thing twice while the first is still open answers 409.</para>
    /// </summary>
    public Task<ApiOutcome> FileReportAsync(
        string subjectKind, string subjectId, string category, string? body, CancellationToken ct = default) =>
        PostAsync("/api/v1/reports",
            new { subjectKind, subjectId, category, body }, ct);

    /// <summary>The site notice as stored. Moderators read this to edit it; the banner uses the cache.</summary>
    public Task<SiteNotice?> GetNoticeAsync(CancellationToken ct = default) =>
        GetAsync<SiteNotice>("/api/v1/notice", ct);

    /// <summary>Puts a notice up, changes it, or takes it down by sending an empty message.</summary>
    public async Task<ApiOutcome> SetNoticeAsync(
        string message, string variant, string? linkText, string? linkHref, bool dismissible,
        CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.PutAsJsonAsync("/api/v1/admin/notice",
            new { message, variant, linkText, linkHref, dismissible }, Json, ct);

        return response.IsSuccessStatusCode
            ? ApiOutcome.Ok()
            : ApiOutcome.Failed(await ReadProblemAsync(response, ct), response.StatusCode);
    }

    /// <summary>Somebody's public profile and the mods they publish. Null when there is no such handle.</summary>
    public Task<PublicProfile?> GetPublicProfileAsync(string handle, CancellationToken ct = default) =>
        GetAsync<PublicProfile>($"/api/v1/accounts/{Uri.EscapeDataString(handle)}", ct);

    /// <summary>Everyone with a role on a listing. Needs permission to manage it.</summary>
    public Task<MaintainerList?> GetMaintainersAsync(string modId, CancellationToken ct = default) =>
        GetAsync<MaintainerList>($"/api/v1/mods/{Uri.EscapeDataString(modId)}/maintainers", ct);

    public Task<ApiOutcome> AddMaintainerAsync(string modId, string handle, CancellationToken ct = default) =>
        PostAsync($"/api/v1/mods/{Uri.EscapeDataString(modId)}/maintainers", new { handle }, ct);

    /// <summary>Hands the listing to somebody else. The previous owner stays on as a maintainer.</summary>
    public Task<ApiOutcome> TransferOwnershipAsync(string modId, string handle, CancellationToken ct = default) =>
        PostAsync($"/api/v1/mods/{Uri.EscapeDataString(modId)}/owner", new { handle }, ct);

    public async Task<ApiOutcome> RemoveMaintainerAsync(
        string modId, string handle, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var response = await client.DeleteAsync(
            $"/api/v1/mods/{Uri.EscapeDataString(modId)}/maintainers/{Uri.EscapeDataString(handle)}", ct);

        return response.IsSuccessStatusCode
            ? ApiOutcome.Ok()
            : ApiOutcome.Failed(await ReadProblemAsync(response, ct), response.StatusCode);
    }

    private async Task<ApiOutcome> PostAsync(string path, object body, CancellationToken ct)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync(path, body, Json, ct);

        return response.IsSuccessStatusCode
            ? ApiOutcome.Ok()
            : ApiOutcome.Failed(await ReadProblemAsync(response, ct), response.StatusCode);
    }

    /// <summary>Builds a query string from the parameters that were actually given.</summary>
    private static string Query(params (string Name, string? Value)[] parameters)
    {
        var given = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}")
            .ToList();

        return given.Count == 0 ? "" : $"?{string.Join('&', given)}";
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
    [JsonPropertyName("icon_url")] public string? IconUrl { get; init; }
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
    [JsonPropertyName("icon_url")] public string? IconUrl { get; init; }

    /// <summary>owner, maintainer, or null for everyone else. Decides who sees the manage controls.</summary>
    [JsonPropertyName("your_role")] public string? YourRole { get; init; }

    /// <summary>Whose listing this is. Absent only when the owning account has been anonymised.</summary>
    [JsonPropertyName("author")] public ModAuthor? Author { get; init; }

    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
    [JsonPropertyName("releases")] public IReadOnlyList<ReleaseSummary> Releases { get; init; } = [];

    /// <summary>Set when the API served a withdrawal notice in place of the listing.</summary>
    [JsonPropertyName("message")] public string? Message { get; init; }

    public bool IsWithdrawn => ListingState is "delisted" or "taken_down";
    public bool IsDeprecated => Status == "deprecated";

    /// <summary>
    /// Not a mod: the code other mods run on. Kept out of the mod lists, because nobody browses
    /// for one - they install it because something they wanted asked for it.
    /// </summary>
    public bool IsLoader => Type == "mod-loader";

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

public sealed record ModMatch
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("asset_ids")] public IReadOnlyList<string> AssetIds { get; init; } = [];
}

public sealed record CollisionResult
{
    /// <summary>Set when the search named a listing rather than an asset id.</summary>
    [JsonPropertyName("mod_match")] public ModMatch? ModMatch { get; init; }

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
public sealed record ModAuthor
{
    [JsonPropertyName("handle")] public string Handle { get; init; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
}

public sealed record PublicProfile
{
    [JsonPropertyName("handle")] public string Handle { get; init; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("bio")] public string? Bio { get; init; }
    [JsonPropertyName("links")] public Dictionary<string, string> Links { get; init; } = [];
    [JsonPropertyName("forums_url")] public string? ForumsUrl { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("mods")] public IReadOnlyList<ProfileMod> Mods { get; init; } = [];
}

public sealed record ProfileMod
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("abstract")] public string Abstract { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "mod";
    [JsonPropertyName("tags")] public IReadOnlyList<string> Tags { get; init; } = [];
    [JsonPropertyName("icon_url")] public string? IconUrl { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record MaintainerList
{
    [JsonPropertyName("maintainers")] public IReadOnlyList<Maintainer> Maintainers { get; init; } = [];
}

public sealed record Maintainer
{
    [JsonPropertyName("handle")] public string Handle { get; init; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    [JsonPropertyName("role")] public string Role { get; init; } = "maintainer";
    [JsonPropertyName("added_at")] public DateTimeOffset AddedAt { get; init; }

    public bool IsOwner => Role == "owner";
}

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
    [JsonPropertyName("bio")] public string? Bio { get; init; }
    [JsonPropertyName("links")] public Dictionary<string, string> Links { get; init; } = [];
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

public sealed record OwnedModStatus
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "mod";
    [JsonPropertyName("role")] public string Role { get; init; } = "maintainer";
    [JsonPropertyName("listing_state")] public string ListingState { get; init; } = "listed";
    [JsonPropertyName("status")] public string Status { get; init; } = "active";
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("releases")] public int Releases { get; init; }
    [JsonPropertyName("failed_releases")] public int FailedReleases { get; init; }
    [JsonPropertyName("latest_version")] public string? LatestVersion { get; init; }
    [JsonPropertyName("latest_released_at")] public DateTimeOffset? LatestReleasedAt { get; init; }
    [JsonPropertyName("repo_connected")] public bool RepoConnected { get; init; }
    [JsonPropertyName("repo_verified")] public bool RepoVerified { get; init; }
    [JsonPropertyName("repo_full_name")] public string? RepoFullName { get; init; }
    [JsonPropertyName("import_running")] public bool ImportRunning { get; init; }
    [JsonPropertyName("last_import_error")] public string? LastImportError { get; init; }

    public bool IsDraft => ListingState == "unlisted";
    public bool IsWithdrawn => ListingState is "delisted" or "taken_down";
    public bool IsOwner => Role == "owner";

    /// <summary>
    /// The one thing to do next, or null when the listing is simply fine.
    ///
    /// <para>Ordered by what blocks what: an unconnected repository makes importing impossible,
    /// an unverified one makes it refused, no releases makes publishing pointless. Showing all of
    /// them at once would be a list of everything that is not yet true rather than a next step.</para>
    /// </summary>
    public string? NextStep =>
        IsWithdrawn ? null
        : !RepoConnected
            ? IsOwner
                ? "Connect the repository you release from."
                : "Waiting on the owner to connect a repository."
        : !RepoVerified
            ? IsOwner
                ? "Prove the repository is yours."
                : "Waiting on the owner to verify the repository."
        : Releases == 0 ? "Tag a release and import it."
        : LastImportError is not null ? "The last import failed."
        : FailedReleases > 0 ? $"{FailedReleases} release(s) failed validation."
        : IsDraft
            ? IsOwner
                ? "Still a draft. Publish it when you are ready."
                : "Still a draft. Only the owner can publish it."
        : null;

    /// <summary>
    /// Whether the next step is one this person can actually take.
    ///
    /// <para>Connecting a repository, verifying it and publishing are the owner's alone (§4.2), so
    /// putting a maintainer's listing under "Waiting on you" with a button the API will refuse
    /// would be sending them to a locked door. They still see the state - it explains why nothing
    /// is importing - it just is not their queue.</para>
    /// </summary>
    public bool NextStepIsYours =>
        NextStep is not null && (IsOwner || (RepoConnected && RepoVerified && !IsDraft));

    public bool NeedsAttention => NextStepIsYours;
}

public sealed record AccountSession
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("issued_at")] public DateTimeOffset IssuedAt { get; init; }
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; init; }
    [JsonPropertyName("user_agent")] public string? UserAgent { get; init; }
    [JsonPropertyName("current")] public bool Current { get; init; }
}

public sealed record UpdateProfileRequest(
    string? DisplayName, string? Handle, string? ForumsUrl,
    string? Bio = null, Dictionary<string, string>? Links = null);

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

// ── tags ──

public sealed record TagListPage
{
    [JsonPropertyName("items")] public IReadOnlyList<TagOption> Items { get; init; } = [];
}

public sealed record TagOption
{
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("label")] public string Label { get; init; } = "";
    [JsonPropertyName("description")] public string? Description { get; init; }
}

public sealed record TagProposalResponse
{
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("state")] public string State { get; init; } = "proposed";
    [JsonPropertyName("note")] public string? Note { get; init; }

    /// <summary>True when the tag turned out to exist already and can be used immediately.</summary>
    public bool UsableNow => State == "approved";
}

public sealed record TagProposalResult(bool Success, TagProposalResponse? Response, string? Error);

public sealed record AdminTag
{
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("label")] public string Label { get; init; } = "";
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("state")] public string State { get; init; } = "proposed";
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("proposer_handle")] public string? ProposerHandle { get; init; }
    [JsonPropertyName("review_note")] public string? ReviewNote { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("reviewed_at")] public DateTimeOffset? ReviewedAt { get; init; }
    [JsonPropertyName("uses")] public int Uses { get; init; }

    public bool IsPending => State == "proposed";
}

public sealed record UnknownTag
{
    [JsonPropertyName("tag")] public string Tag { get; init; } = "";
    [JsonPropertyName("uses")] public int Uses { get; init; }
}

// ── moderation ──

public sealed record AdminOverview
{
    [JsonPropertyName("open_reports")] public int OpenReports { get; init; }
    [JsonPropertyName("quarantined")] public int Quarantined { get; init; }
    [JsonPropertyName("needs_review")] public int NeedsReview { get; init; }
    [JsonPropertyName("dead_jobs")] public int DeadJobs { get; init; }
    [JsonPropertyName("pending_tags")] public int PendingTags { get; init; }
    [JsonPropertyName("withdrawn")] public int Withdrawn { get; init; }
    [JsonPropertyName("suspended")] public int Suspended { get; init; }
    [JsonPropertyName("accounts")] public int Accounts { get; init; }
    [JsonPropertyName("mods")] public int Mods { get; init; }
    [JsonPropertyName("releases")] public int Releases { get; init; }
}

public sealed record AdminReport
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("subject_kind")] public string SubjectKind { get; init; } = "";
    [JsonPropertyName("subject_id")] public string SubjectId { get; init; } = "";
    [JsonPropertyName("category")] public string Category { get; init; } = "";
    [JsonPropertyName("body")] public string? Body { get; init; }
    [JsonPropertyName("state")] public string State { get; init; } = "open";
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("resolved_at")] public DateTimeOffset? ResolvedAt { get; init; }
    [JsonPropertyName("reporter_handle")] public string? ReporterHandle { get; init; }
    [JsonPropertyName("resolved_by")] public string? ResolvedBy { get; init; }

    public bool IsOpen => State == "open";
}

public sealed record AdminReview
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("mod_id")] public string ModId { get; init; } = "";
    [JsonPropertyName("mod_name")] public string ModName { get; init; } = "";
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("released_at")] public DateTimeOffset ReleasedAt { get; init; }
    [JsonPropertyName("availability")] public string Availability { get; init; } = "";
    [JsonPropertyName("validation_state")] public string ValidationState { get; init; } = "";
    [JsonPropertyName("ships_code")] public bool ShipsCode { get; init; }
    [JsonPropertyName("runs_console")] public bool RunsConsole { get; init; }
    [JsonPropertyName("quarantined")] public bool Quarantined { get; init; }
    [JsonPropertyName("warnings")] public int Warnings { get; init; }
    [JsonPropertyName("open_reports")] public int OpenReports { get; init; }
}

public sealed record AdminListing
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("listing_state")] public string ListingState { get; init; } = "listed";
    [JsonPropertyName("status")] public string Status { get; init; } = "active";
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("owner_handle")] public string? OwnerHandle { get; init; }
    [JsonPropertyName("releases")] public int Releases { get; init; }
    [JsonPropertyName("open_reports")] public int OpenReports { get; init; }

    public bool IsWithdrawn => ListingState is "delisted" or "taken_down";
}

public sealed record AdminAccount
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("handle")] public string Handle { get; init; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("site_role")] public string SiteRole { get; init; } = "user";
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("suspended_at")] public DateTimeOffset? SuspendedAt { get; init; }
    [JsonPropertyName("deleted_at")] public DateTimeOffset? DeletedAt { get; init; }
    [JsonPropertyName("mods")] public int Mods { get; init; }
    [JsonPropertyName("sessions")] public int Sessions { get; init; }

    public bool IsSuspended => SuspendedAt is not null;
    public bool IsDeleted => DeletedAt is not null;
}

public sealed record ModerationEntry
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("action")] public string Action { get; init; } = "";
    [JsonPropertyName("subject_kind")] public string SubjectKind { get; init; } = "";
    [JsonPropertyName("subject_id")] public string SubjectId { get; init; } = "";
    [JsonPropertyName("rationale")] public string Rationale { get; init; } = "";
    [JsonPropertyName("public")] public bool Public { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("supersedes")] public long? Supersedes { get; init; }
    [JsonPropertyName("actor_handle")] public string? ActorHandle { get; init; }
}

public sealed record AdminJob
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("state")] public string State { get; init; } = "";
    [JsonPropertyName("attempts")] public int Attempts { get; init; }
    [JsonPropertyName("run_after")] public DateTimeOffset RunAfter { get; init; }
    [JsonPropertyName("last_error")] public string? LastError { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }

    public bool CanRetry => State is "dead" or "failed";
}

// ── requests ──

public sealed record CreateModRequest(
    string Id, string Name, string Abstract, string License,
    string? Description = null, string[]? Tags = null,
    Dictionary<string, string>? Links = null, string? BannerUrl = null, string? IconUrl = null);

/// <summary>Null means "leave this one alone", so a partial edit stays partial.</summary>
public sealed record EditModRequest(
    string? Name = null, string? Abstract = null, string? Description = null,
    string? License = null, string[]? Tags = null,
    Dictionary<string, string>? Links = null, string? BannerUrl = null, string? IconUrl = null);

/// <summary>
/// RepoId is no longer asked of the author: the API resolves it from the forge, which is one less
/// number to go and find and one less way to connect the wrong repository.
/// </summary>
public sealed record ConnectRepoRequest(
    string Provider, string RepoFullName, string? AssetGlob = null,
    string? RepoId = null, string? InstallationId = null);

public sealed record RepoLinkResult(bool Success, RepoLinkChallenge? Challenge, string? Error);

public sealed record RepoLinkChallenge
{
    [JsonPropertyName("repo_full_name")] public string RepoFullName { get; init; } = "";
    [JsonPropertyName("default_branch")] public string DefaultBranch { get; init; } = "main";
    [JsonPropertyName("verified")] public bool Verified { get; init; }
    [JsonPropertyName("challenge")] public string Challenge { get; init; } = "";
    [JsonPropertyName("file_path")] public string FilePath { get; init; } = ".ksamods-verify";
    [JsonPropertyName("instructions")] public string Instructions { get; init; } = "";
}

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
