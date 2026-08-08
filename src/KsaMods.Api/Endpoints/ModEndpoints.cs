using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Api.Domain;
using KsaMods.Metadata;

namespace KsaMods.Api.Endpoints;

public sealed record CreateModBody(
    string Id, string Name, string Abstract, string License,
    string? Description, string[]? Tags, Dictionary<string, string>? Links);

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
                ListingState = "listed",
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

        api.MapPost("/mods/{id}/repo-link", async (
            string id, ConnectRepoBody body, HttpContext http,
            ModRepository mods, Database database, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (principal is null) return Results.Unauthorized();

            // Only the owner. A maintainer who could re-point the repository could quietly take
            // over the listing, since the repository link is the ownership proof.
            if (!Permissions.Allows(principal, Capability.ConnectRepository)) return Results.Forbid();

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
            if (!Permissions.Allows(principal, Capability.ImportRelease)) return Results.Forbid();

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
            if (!Permissions.Allows(principal, Capability.YankRelease)) return Results.Forbid();

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
            if (!Permissions.Allows(principal, Capability.AmendRelease)) return Results.Forbid();

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
