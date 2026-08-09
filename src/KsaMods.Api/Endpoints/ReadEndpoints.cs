using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Api.Domain;
using KsaMods.Metadata;
using KsaMods.Resolver;

namespace KsaMods.Api.Endpoints;

/// <summary>
/// The unauthenticated read surface (backend.md §10.1).
///
/// <para>Two rules hold everywhere here: every artifact carries its <c>sha256</c>, because a
/// client that cannot verify is non-conforming and the API's job is to make verifying the easy
/// path; and every document carries <c>spec_version</c>, so a client meeting a version it does
/// not implement renders the entry as unknown rather than dropping it.</para>
/// </summary>
public static class ReadEndpoints
{
    public static void MapReadEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireRateLimiting("reads");

        // /mods is a permanent alias for /content?type=mod. Type is a filter, not a route -
        // RFC 0025 puts vehicles and saves in scope - but breaking an early client's URL to
        // prove the point helps nobody.
        api.MapGet("/mods", SearchAsync);
        api.MapGet("/content", SearchAsync);

        api.MapGet("/mods/{id}", async (
            string id, HttpContext http, ModRepository mods, CancellationToken ct) =>
        {
            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            var principal = await http.PrincipalForModAsync(mods, id, ct);

            if (mod.ListingState is "delisted" or "taken_down" && principal?.IsModerator != true)
            {
                // Delisted content stays resolvable by id - modlists pin it and dependency graphs
                // reference it, and a hole in the graph is worse than a listing marked delisted.
                // The record is served; the downloads are not.
                return Results.Ok(new
                {
                    spec_version = 1,
                    id = mod.Id,
                    listing_state = mod.ListingState,
                    message = "This listing has been withdrawn by moderators. Its releases are not offered.",
                });
            }

            var releases = await mods.ReleasesAsync(mod.Id, ct);
            var owner = await mods.OwnerAsync(mod.Id, ct);

            return Results.Ok(new
            {
                spec_version = 1,
                id = mod.Id,
                type = mod.Type,

                // Who is answerable for this listing. Absent only if the owner's account is gone,
                // which anonymisation leaves behind rather than deleting the listing with it.
                author = owner is null ? null : new
                {
                    handle = owner.Handle,
                    display_name = owner.DisplayName,
                    avatar_url = owner.AvatarUrl,
                },

                name = mod.Name,
                @abstract = mod.Abstract,
                description = mod.Description,
                license = mod.License,
                tags = mod.Tags,
                status = mod.Status,
                superseded_by = mod.SupersededBy,
                listing_state = mod.ListingState,
                banner_url = mod.BannerUrl,
                icon_url = mod.IconUrl,

                // The caller's own role on this listing, so the frontend can offer management
                // controls only to people they will work for. The principal is already loaded
                // above for the visibility check, so this costs nothing extra.
                your_role = principal?.ModRole,
                updated_at = mod.UpdatedAt,
                releases = releases
                    .Where(r => Permissions.CanViewRelease(principal, r.ValidationState, mod.ListingState))
                    .Select(r => Summarise(r)),
            });
        });

        api.MapGet("/mods/{id}/releases/{version}", async (
            string id, string version, HttpContext http,
            ModRepository mods, Database database, CancellationToken ct) =>
        {
            var mod = await mods.FindAsync(id, ct);
            if (mod is null) return Results.NotFound();

            var releases = await mods.ReleasesAsync(mod.Id, ct);
            var release = releases.FirstOrDefault(r =>
                string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase));

            if (release is null) return Results.NotFound();

            var principal = await http.PrincipalForModAsync(mods, id, ct);
            if (!Permissions.CanViewRelease(principal, release.ValidationState, mod.ListingState))
            {
                return Results.NotFound();
            }

            using var connection = await database.OpenAsync(ct);

            var artifacts = await connection.QueryAsync<(string Url, byte[] Sha256, long Size, string ContentType, bool IsMirror)>("""
                select url, sha256, size, content_type, is_mirror
                from release_artifact where release_id = @id
                """,
                new { id = release.Id });

            var findings = await mods.FindingsAsync(release.Id, ct);

            var dependencies = await connection.QueryAsync<(string? DepId, string Kind, string? MinVersion, string? MaxVersion, string Source)>("""
                select dep_id, kind, min_version, max_version, source
                from release_dependency where release_id = @id
                order by dep_id
                """,
                new { id = release.Id });

            var console = await connection.QueryAsync<(string Hook, int Ordinal, string Command)>("""
                select hook, ordinal, command from release_console
                where release_id = @id order by hook, ordinal
                """,
                new { id = release.Id });

            return Results.Ok(new
            {
                spec_version = 1,
                id = mod.Id,
                version = release.Version,
                release_status = release.ReleaseStatus,
                released_at = release.ReleasedAt,
                game_min = release.GameMinDisplay,
                game_min_revision = release.GameMinRevision,
                game_max = release.GameMaxDisplay,
                game_max_revision = release.GameMaxRevision,
                install = release.InstallRoot is null ? null : new { root = release.InstallRoot },
                install_size = release.InstallSize,
                loader = release.LoaderId is null
                    ? null
                    : new { id = release.LoaderId, min = release.LoaderMin, max = release.LoaderMax },
                availability = release.Availability,
                last_verified_at = release.LastVerifiedAt,
                validation_state = release.ValidationState,
                yanked = release.YankedAt is not null,
                yanked_reason = release.YankedReason,
                changelog = release.ChangelogUrl,
                download = artifacts
                    .Where(a => !a.IsMirror)
                    .Select(a => new
                    {
                        url = a.Url,
                        // Never optional, never behind a flag.
                        sha256 = Convert.ToHexStringLower(a.Sha256),
                        size = a.Size,
                        content_type = a.ContentType,
                    })
                    .FirstOrDefault(),
                mirrors = artifacts.Where(a => a.IsMirror).Select(a => a.Url),
                dependencies = dependencies.Select(d => new
                {
                    id = d.DepId,
                    kind = d.Kind,
                    min = d.MinVersion,
                    max = d.MaxVersion,
                    source = d.Source,
                }),
                // Displayed verbatim; never silently stripped.
                console = console.Select(c => new { hook = c.Hook, ordinal = c.Ordinal, command = c.Command }),
                findings = findings.Select(f => new
                {
                    stage = f.Stage,
                    severity = f.Severity.ToString().ToLowerInvariant(),
                    code = f.Code,
                    message = f.Message,
                    path = f.Path,
                }),
            });
        });

        api.MapGet("/collisions/{assetId}", async (
            string assetId, ModRepository mods, CancellationToken ct) =>
        {
            var owners = await mods.CollisionsAsync(assetId, ct);

            return Results.Ok(new
            {
                asset_id = assetId,
                declared_by = owners.Select(o => new { id = o.ModId, version = o.Version, xml_path = o.XmlPath }),
                // The explanation matters as much as the data: this failure mode is invisible
                // in-game, so a bare list would not tell anyone why it matters.
                note = owners.Count > 1
                    ? "KSA registers asset ids into one global table with TryAdd, so only the first mod to load registers this id. The others are silently discarded."
                    : null,
            });
        });

        api.MapGet("/builds", async (Database database, CancellationToken ct) =>
        {
            using var connection = await database.OpenAsync(ct);

            var builds = await connection.QueryAsync<(int Revision, string VersionString, DateTime? ReleasedOn)>(
                "select revision, version_string, released_on from build order by revision desc");

            return Results.Ok(builds.Select(b => new
            {
                revision = b.Revision,
                build = b.VersionString,
                date = b.ReleasedOn,
            }));
        });

        api.MapPost("/resolve", async (
            ResolveBody body, Database database, CancellationToken ct) =>
        {
            var catalogue = await CatalogueLoader.LoadAsync(database, ct);
            var planner = new InstallPlanner(catalogue);

            var plan = planner.Resolve(new ResolveRequest
            {
                Targets =
                [
                    .. body.Content.Select(c => new ResolveTarget(
                        c.Id,
                        c.Version is null ? null : SemVer.Parse(c.Version))),
                ],
                GameRevision = body.GameBuild,
                IncludeRecommended = body.IncludeRecommended ?? true,
            });

            return Results.Ok(new
            {
                satisfiable = plan.Satisfiable,
                // The order to write into manifest.toml - not a promise about initialisation
                // order, which StarMap reorders through its waiting graph.
                order = plan.Order.Select(p => new
                {
                    id = p.ModId,
                    version = p.Version.ToString(),
                    compatibility = p.Compatibility.ToString().ToLowerInvariant(),
                    reason = p.Reason,
                    overrides_core = p.OverridesCore,
                }),
                collisions = plan.Collisions.Select(c => new { asset_id = c.AssetId, mods = c.ModIds }),
                problems = plan.Problems.Select(p => new
                {
                    kind = p.Kind.ToString(),
                    id = p.ModId,
                    detail = p.Detail,
                }),
                suggested = plan.Suggested,
            });
        }).RequireRateLimiting("writes");
    }

    private static async Task<IResult> SearchAsync(
        Database database, string? q, string? type, string? tag, int? gameBuild,
        string? after, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);

        using var connection = await database.OpenAsync(ct);

        // Keyset pagination, not offset: offset over a table changing mid-scroll skips and
        // repeats rows, and the offline-snapshot use case makes stable iteration a requirement.
        var rows = await connection.QueryAsync<(string Id, string Type, string Name, string Abstract, string[] Tags, DateTimeOffset UpdatedAt, string? BannerUrl, string? IconUrl)>("""
            select m.id, m.type, m.name, m.abstract, m.tags, m.updated_at, m.banner_url, m.icon_url
            from mod m
            where m.listing_state = 'listed'
              and (@type is null or m.type = @type)
              and (@tag is null or @tag = any(m.tags))
              and (@q is null or m.name ilike '%' || @q || '%' or m.abstract ilike '%' || @q || '%')
              and (@after is null or m.id_lower > @after)
              and (@gameBuild is null or exists (
                    select 1 from mod_release r
                    where r.mod_id = m.id
                      and r.yanked_at is null
                      and (r.game_min_revision is null or r.game_min_revision <= @gameBuild)))
            order by m.id_lower
            limit @take
            """,
            new { q, type, tag, gameBuild, after = after?.ToLowerInvariant(), take });

        var list = rows.ToList();

        return Results.Ok(new
        {
            spec_version = 1,
            items = list.Select(r => new
            {
                id = r.Id,
                type = r.Type,
                name = r.Name,
                @abstract = r.Abstract,
                tags = r.Tags,
                updated_at = r.UpdatedAt,
                banner_url = r.BannerUrl,
                icon_url = r.IconUrl,
            }),
            next = list.Count == take ? list[^1].Id : null,
        });
    }

    private static object Summarise(ReleaseRow r) => new
    {
        version = r.Version,
        release_status = r.ReleaseStatus,
        released_at = r.ReleasedAt,
        game_min_revision = r.GameMinRevision,
        game_max_revision = r.GameMaxRevision,
        availability = r.Availability,
        validation_state = r.ValidationState,
        yanked = r.YankedAt is not null,
    };
}

public sealed record ResolveBody(
    IReadOnlyList<ResolveContentRef> Content,
    int? GameBuild,
    bool? IncludeRecommended);

public sealed record ResolveContentRef(string Id, string? Version);
