using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Api.Domain;
using KsaMods.Metadata;

namespace KsaMods.Api.Endpoints;

public sealed record CreateModBody(
    string Id, string Name, string Abstract, string License,
    string? Description, string[]? Tags, Dictionary<string, string>? Links,
    string? BannerUrl = null);

/// <summary>
/// Every field optional: a caller sending one field changes one field. No id, because the id is
/// the folder name the game loads and cannot move.
/// </summary>
public sealed record EditModBody(
    string? Name, string? Abstract, string? Description, string? License,
    string[]? Tags, Dictionary<string, string>? Links, string? BannerUrl);

public sealed record ConnectRepoBody(string Provider, string RepoId, string RepoFullName, string? InstallationId, string? AssetGlob);

public sealed record YankBody(string Reason);

public static class ModEndpoints
{
    public static void MapModEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireRateLimiting("writes");

        api.MapPost("/mods", async (
            CreateModBody body, HttpContext http, ModRepository mods, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            if (!ContentId.TryParse(body.Id, out var id, out var reason))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["id"] = [Explain(reason)],
                });
            }

            // Checked across mods, modlists and retired modlist aliases in one statement: the
            // namespace is global, so two lookups would leave a race between them.
            if (!await mods.IsIdAvailableAsync(id.Value.Value, ct))
            {
                return Results.Conflict(new
                {
                    error = "id_taken",
                    detail = $"'{body.Id}' is already claimed. Ids are compared case-insensitively across mods and modlists.",
                });
            }

            // The column has the same rule as a check constraint, but a constraint violation
            // surfaces as a 500. Reject it here so the author gets told what is wrong with it.
            if (!string.IsNullOrWhiteSpace(body.BannerUrl) && !IsUsableBannerUrl(body.BannerUrl))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["bannerUrl"] = ["A banner must be an https:// link to an image, under 2048 characters."],
                });
            }

            var links = body.Links ?? [];

            await mods.CreateAsync(new ModRow
            {
                Id = id.Value.Value,
                Type = ContentType.Mod,
                Name = body.Name,
                Abstract = body.Abstract,
                Description = body.Description,
                License = body.License,
                Tags = body.Tags ?? [],
                Links = System.Text.Json.JsonSerializer.Serialize(links),
                Status = "active",

                // Created as a draft. A listing is useless until it has a repository and a
                // release, and publishing the empty shell straight into browse means every
                // half-finished idea shows up in search. The author publishes it when it is
                // worth looking at.
                ListingState = "unlisted",
                BannerUrl = string.IsNullOrWhiteSpace(body.BannerUrl) ? null : body.BannerUrl.Trim(),
                CreatedBy = user.AccountId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }, user.AccountId, ct);

            return Results.Created($"/api/v1/mods/{id.Value.Value}", new
            {
                id = id.Value.Value,
                // The forums link is not required to create — app installation is the stronger
                // ownership proof — but it is required to appear in the exported index.
                note = links.ContainsKey("forums")
                    ? null
                    : "Add a KSA forums thread under links.forums; it is required for this listing to appear in the exported index.",
            });
        });

        // Editing a listing. The id is absent on purpose: it is the folder name the game loads
        // the mod under, so it cannot change without breaking every install of it.
        api.MapPatch("/mods/{id}", async (
            string id, EditModBody body, HttpContext http, ModRepository mods, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.EditModListing)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(body.BannerUrl) && !IsUsableBannerUrl(body.BannerUrl))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["bannerUrl"] = ["A banner must be an https:// link to an image, under 2048 characters."],
                });
            }

            // Absent fields keep their current value, so a caller sending only one field does not
            // silently blank the rest.
            await mods.UpdateAsync(mod with
            {
                Name = body.Name ?? mod.Name,
                Abstract = body.Abstract ?? mod.Abstract,
                Description = body.Description ?? mod.Description,
                License = body.License ?? mod.License,
                Tags = body.Tags ?? mod.Tags,
                Links = body.Links is null
                    ? mod.Links
                    : System.Text.Json.JsonSerializer.Serialize(body.Links),
                BannerUrl = string.IsNullOrWhiteSpace(body.BannerUrl) ? null : body.BannerUrl.Trim(),
            }, ct);

            return Results.NoContent();
        });

        // Publish and unlist are two endpoints rather than a state field on the patch above.
        // "Make this visible to everyone" is a decision, not a property edit, and it carries a
        // different permission.
        api.MapPost("/mods/{id}/publish", (string id, HttpContext http, ModRepository mods, CancellationToken ct) =>
            SetVisibilityAsync(id, "listed", http, mods, ct));

        api.MapPost("/mods/{id}/unlist", (string id, HttpContext http, ModRepository mods, CancellationToken ct) =>
            SetVisibilityAsync(id, "unlisted", http, mods, ct));

        // Delete, but only while nothing points at the listing.
        //
        // The site tells people ids stay resolvable so their modlists do not break, and that has
        // to hold even when an author changes their mind. Once a listing has a release, or
        // anything pins or depends on it, the answer is unlisting instead. A listing created by
        // mistake has none of that, and removing it costs nobody anything.
        api.MapDelete("/mods/{id}", async (
            string id, HttpContext http, ModRepository mods, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.DeleteMod)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            var references = await mods.ReferencesAsync(mod.Id, ct);
            if (!references.IsUnused)
            {
                return Results.Conflict(new
                {
                    error = "mod_in_use",
                    detail = references.Explain(),
                });
            }

            await mods.DeleteAsync(mod.Id, ct);
            return Results.NoContent();
        });

        api.MapPost("/mods/{id}/repo-link", async (
            string id, ConnectRepoBody body, HttpContext http,
            ModRepository mods, Database database, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();

            // Only the owner. A maintainer who could re-point the repository could quietly take
            // over the listing, since the repository link is the ownership proof.
            if (!Permissions.Allows(principal, Capability.ConnectRepository)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            using var connection = await database.OpenAsync(ct);

            var existing = await connection.ExecuteScalarAsync<string?>(
                "select mod_id from repo_link where provider = @provider and repo_id = @repoId",
                new { provider = body.Provider, repoId = body.RepoId });

            if (existing is not null && !string.Equals(existing, mod.Id, StringComparison.OrdinalIgnoreCase))
            {
                // A repository already linked elsewhere is a dispute, not an error to work around.
                return Results.Conflict(new
                {
                    error = "repository_already_linked",
                    detail = $"That repository is already connected to '{existing}'. If you believe this is wrong, open a dispute.",
                });
            }

            await connection.ExecuteAsync("""
                insert into repo_link (mod_id, provider, installation_id, repo_id, repo_full_name, linked_by, asset_glob)
                values (@modId, @provider, @installationId, @repoId, @repoFullName, @linkedBy, @assetGlob)
                on conflict (mod_id) do update set
                    provider = excluded.provider,
                    installation_id = excluded.installation_id,
                    repo_id = excluded.repo_id,
                    repo_full_name = excluded.repo_full_name,
                    linked_by = excluded.linked_by,
                    asset_glob = excluded.asset_glob,
                    linked_at = now()
                """,
                new
                {
                    modId = mod.Id,
                    provider = body.Provider,
                    installationId = body.InstallationId,
                    repoId = body.RepoId,
                    repoFullName = body.RepoFullName,
                    linkedBy = principal.AccountId,
                    assetGlob = body.AssetGlob,
                });

            return Results.NoContent();
        });

        api.MapPost("/mods/{id}/releases/import", async (
            string id, HttpContext http, ModRepository mods, JobQueue jobs, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.ImportRelease)) return ApiResults.Forbidden();

            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            var jobId = await jobs.EnqueueAsync("import_release", new { modId = mod.Id }, ct);

            // Importing runs the pipeline in a container and takes seconds to minutes; the UI
            // polls rather than blocking.
            return Results.Accepted($"/api/v1/jobs/{jobId}", new { job_id = jobId, state = "queued" });
        });

        api.MapPost("/mods/{id}/releases/{version}/yank", async (
            string id, string version, YankBody body, HttpContext http,
            ModRepository mods, Database database, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.YankRelease)) return ApiResults.Forbidden();

            if (string.IsNullOrWhiteSpace(body.Reason))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reason"] = ["A yank must say why: it is shown to anyone who has this version installed."],
                });
            }

            using var connection = await database.OpenAsync(ct);

            var affected = await connection.ExecuteAsync("""
                update mod_release
                set yanked_at = now(), yanked_reason = @reason
                where mod_id = (select id from mod where id_lower = @modId)
                  and version = @version
                  and yanked_at is null
                """,
                new { modId = id.ToLowerInvariant(), version, reason = body.Reason });

            // A yank is the author's statement about one build — distinct from `deprecated`,
            // which covers the whole listing, and from a moderator delisting.
            return affected == 0 ? Results.NotFound() : Results.NoContent();
        });

        api.MapPatch("/mods/{id}/releases/{version}", async (
            string id, string version, ReleaseFacts proposed, HttpContext http,
            ModRepository mods, Database database, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.AmendRelease)) return ApiResults.Forbidden();

            var releases = await mods.ReleasesAsync(id, ct);
            var release = releases.FirstOrDefault(r =>
                string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase));

            if (release is null) return Results.NotFound();

            using var connection = await database.OpenAsync(ct);

            var dependencies = await connection.QueryAsync<DependencyFact>("""
                select dep_id as DepId, kind as Kind, min_version as Min, max_version as Max
                from release_dependency where release_id = @id and dep_id is not null
                """,
                new { id = release.Id });

            var current = new ReleaseFacts
            {
                GameMinRevision = release.GameMinRevision,
                GameMaxRevision = release.GameMaxRevision,
                LoaderMin = release.LoaderMin,
                LoaderMax = release.LoaderMax,
                Dependencies = [.. dependencies],
                Yanked = release.YankedAt is not null,
            };

            // The invariant that makes "immutable" mean something: a release can never become
            // more permissive after publication.
            var rejections = ReleaseAmendment.Check(current, proposed);
            if (rejections.Count > 0)
            {
                return Results.BadRequest(new
                {
                    error = "amendment_widens",
                    detail = "A published release may only ever be narrowed. Publish a new version instead.",
                    rejections = rejections.Select(r => new { field = r.Field, reason = r.Reason }),
                });
            }

            await connection.ExecuteAsync("""
                update mod_release
                set game_min_revision = @GameMinRevision,
                    game_max_revision = @GameMaxRevision,
                    loader_min = @LoaderMin,
                    loader_max = @LoaderMax
                where id = @id
                """,
                new
                {
                    id = release.Id,
                    proposed.GameMinRevision,
                    proposed.GameMaxRevision,
                    proposed.LoaderMin,
                    proposed.LoaderMax,
                });

            return Results.NoContent();
        });
    }

    /// <summary>
    /// Shared by publish and unlist. Only ever moves between 'listed' and 'unlisted', so a
    /// moderator's delisting cannot be cleared by the author it was applied to.
    /// </summary>
    private static async Task<IResult> SetVisibilityAsync(
        string id, string state, HttpContext http, ModRepository mods, CancellationToken ct)
    {
        var principal = await http.PrincipalForModAsync(mods, id, ct);
        if (principal is null) return Results.Unauthorized();
        if (!Permissions.Allows(principal, Capability.SetModVisibility)) return ApiResults.Forbidden();

        var mod = await mods.FindAsync(id, ct);
        if (mod is null) return Results.NotFound();

        if (!await mods.SetListingStateAsync(mod.Id, state, ct))
        {
            return Results.Conflict(new
            {
                error = "listing_locked",
                detail = "This listing has been withdrawn by moderators, so its visibility is not yours to change.",
            });
        }

        return Results.NoContent();
    }

    /// <summary>
    /// A banner is a link to an image the author already hosts, so the only things we can check
    /// are the ones that decide whether a browser will render it at all: https, because the page
    /// is https and mixed content is blocked silently, and a length the column will accept.
    /// </summary>
    private static bool IsUsableBannerUrl(string candidate) =>
        candidate.Trim() is { Length: > 0 and <= 2048 } url
        && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && parsed.Scheme == Uri.UriSchemeHttps;

    private static string Explain(IdRejection reason) => reason switch
    {
        IdRejection.Empty => "An id is required.",
        IdRejection.Length => "An id must be 1 to 64 characters.",
        IdRejection.Boundary => "An id must start and end with a letter or digit.",
        IdRejection.Charset => "An id may contain only ASCII letters, digits, '.', '-' and '_'.",
        IdRejection.Reserved => "That name is reserved by the game or by Windows.",
        _ => "That id is not valid.",
    };
}
