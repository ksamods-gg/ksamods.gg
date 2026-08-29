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

            var principal = await http.ReadPrincipalForModAsync(mods, id, ct);

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

                // Who is answerable for this listing. Absent if the owner's account is gone, which
                // anonymisation leaves behind rather than deleting the listing with it, and absent
                // if the author asked not to be named.
                //
                // The hidden case still answers for people who already know: anyone holding a role
                // on the listing, and moderators, who need it to act on a takedown. Everyone else
                // gets null and the flag beside it, so a client can say "author hidden" instead of
                // rendering the same blank as a deleted account.
                author = owner is null || (mod.HideAuthor && !MaySeeHiddenAuthor(principal)) ? null : new
                {
                    handle = owner.Handle,
                    display_name = owner.DisplayName,
                    avatar_url = owner.AvatarUrl,
                },
                author_hidden = mod.HideAuthor,

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

                // The display string, not the revision. The revision is what orders, but it is
                // an implementation fact of RFC 0017; what a reader recognises is the version the
                // game shows them, and what an author needs back is what they typed.
                game_min = mod.GameMinDisplay,
                game_max = mod.GameMaxDisplay,

                // Where the listing is arbitrated. An 'index' listing is owned upstream: it has
                // author names rather than accounts here, and nothing on this site may edit it.
                source = mod.Source,
                authors = mod.Authors,

                // The caller's own role on this listing, so the frontend can offer management
                // controls only to people they will work for. The principal is already loaded
                // above for the visibility check, so this costs nothing extra.
                your_role = principal?.ModRole,

                // Whether they may manage it at all, which is not the same question. A moderator
                // holds no role on the listing, so your_role stays honestly null for them while
                // this is true. The frontend used to derive this from the role and therefore hid
                // the Manage button from the very people moderation exists for.
                //
                // Answered here rather than recomputed in the client, because it is the same
                // decision the write endpoints make and two copies of an authorisation rule is
                // how they drift apart.
                can_manage = principal is not null
                             && Permissions.Allows(principal, Capability.EditModListing),

                // Owner-level reach, held by the owner and by staff. Splits the controls that
                // change who a listing belongs to from the ones that only change what it says.
                can_manage_as_owner = principal is not null
                                      && Permissions.Allows(principal, Capability.ManageMaintainers),
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

            var principal = await http.ReadPrincipalForModAsync(mods, id, ct);
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

            // Findings a moderator has set aside. Read alongside rather than filtered out in SQL,
            // because they are still served: this site says elsewhere that what the validator found
            // is shown verbatim and never silently stripped, and a warning that vanishes when
            // somebody with a role dislikes it would make that untrue. It is marked, with who set
            // it aside and why, and the client stops treating it as outstanding.
            var suppressed = (await connection.QueryAsync<(string Code, string Reason)>("""
                select code, reason from release_finding_suppression where release_id = @id
                """,
                new { id = release.Id }))
                .ToDictionary(r => r.Code, r => r.Reason, StringComparer.Ordinal);

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

                    // Both fields, always. A client that knows nothing about suppression keeps
                    // showing every finding exactly as before, which is the safe default for a
                    // field that makes a warning quieter.
                    suppressed = suppressed.ContainsKey(f.Code),
                    suppressed_reason = suppressed.GetValueOrDefault(f.Code),
                }),
            });
        });

        api.MapGet("/collisions/{assetId}", async (
            string assetId, ModRepository mods, CancellationToken ct) =>
        {
            var owners = await mods.CollisionsAsync(assetId, ct);

            // Nothing declares it. Before saying so, check whether they typed a mod id, which is
            // the obvious thing to type and the one answer this endpoint cannot give.
            var mod = owners.Count == 0 ? await mods.FindAsync(assetId, ct) : null;
            var declares = mod is null ? [] : await mods.AssetIdsOfAsync(mod.Id, ct);

            return Results.Ok(new
            {
                asset_id = assetId,
                declared_by = owners.Select(o => new { id = o.ModId, version = o.Version, xml_path = o.XmlPath }),

                // Set when the search term names a listing rather than an asset id. The frontend
                // turns it into "that is a mod; here are the asset ids it declares".
                mod_match = mod is null ? null : new
                {
                    id = mod.Id,
                    name = mod.Name,
                    asset_ids = declares,
                },
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

    /// <summary>
    /// Who still sees the author of a listing that hides one.
    ///
    /// <para>Maintainers, because they are looking at their own listing and hiding it from
    /// themselves would only be confusing. Moderators, because a takedown has to land on somebody
    /// and a queue of listings with no visible owner is a queue nobody can act on.</para>
    ///
    /// <para>Nobody else, including signed-in readers. The point of the flag is that a stranger
    /// cannot get from the listing back to the person, and "signed in" is not a relationship.</para>
    /// </summary>
    private static bool MaySeeHiddenAuthor(Principal? principal) =>
        principal is not null && (principal.ModRole is not null || principal.IsModerator);

    private static object Summarise(ReleaseRow r) => new
    {
        version = r.Version,
        release_status = r.ReleaseStatus,
        released_at = r.ReleasedAt,
        game_min_revision = r.GameMinRevision,
        game_max_revision = r.GameMaxRevision,
        availability = r.Availability,

        // When that availability was last established. The site does not host the file, so
        // "verified" is a claim about a moment in the past and is worth little without it.
        last_verified_at = r.LastVerifiedAt,
        validation_state = r.ValidationState,
        yanked = r.YankedAt is not null,
    };
}

public sealed record ResolveBody(
    IReadOnlyList<ResolveContentRef> Content,
    int? GameBuild,
    bool? IncludeRecommended);

public sealed record ResolveContentRef(string Id, string? Version);
