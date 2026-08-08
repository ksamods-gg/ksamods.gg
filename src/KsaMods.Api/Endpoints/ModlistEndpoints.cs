using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Api.Domain;
using KsaMods.Metadata;

namespace KsaMods.Api.Endpoints;

public sealed record CreateModlistBody(
    string Id, string Name, string Abstract, string? Description,
    string? License, string[]? Tags, string? Visibility);

public sealed record DraftEntryBody(string Kind, string TargetId, string? Version, string? Note);

public sealed record PublishBody(string Version, string? Changelog, bool? Confirm);

public sealed record InviteBody(string Handle, string Role);

public static class ModlistEndpoints
{
    public static void MapModlistEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireRateLimiting("writes");

        api.MapPost("/modlists", async (
            CreateModlistBody body, HttpContext http,
            ModRepository mods, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            if (!ContentId.TryParse(body.Id, out var id, out var reason))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["id"] = [$"Not a valid id ({reason})."],
                });
            }

            if (!await mods.IsIdAvailableAsync(id.Value.Value, ct))
            {
                return Results.Conflict(new { error = "id_taken" });
            }

            using var connection = await database.OpenAsync(ct);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            await connection.ExecuteAsync("""
                insert into modlist (id, id_lower, name, abstract, description, license, tags, visibility, created_by)
                values (@id, lower(@id), @name, @abstract, @description, @license, @tags, @visibility, @createdBy)
                """,
                new
                {
                    id = id.Value.Value,
                    name = body.Name,
                    @abstract = body.Abstract,
                    description = body.Description,
                    license = body.License ?? "CC0-1.0",
                    tags = body.Tags ?? [],
                    visibility = body.Visibility ?? "private",
                    createdBy = user.AccountId,
                },
                transaction);

            await connection.ExecuteAsync("""
                insert into modlist_collaborator (modlist_id, account_id, role, accepted_at)
                values (@id, @accountId, 'owner', now())
                """,
                new { id = id.Value.Value, accountId = user.AccountId }, transaction);

            transaction.Commit();

            return Results.Created($"/api/v1/modlists/{id.Value.Value}", new
            {
                id = id.Value.Value,
                // Surprising someone at rename time is worse than warning them at creation time.
                note = "Modlists share the id namespace with mods. If a mod author later needs this exact id as their folder name, the mod takes precedence and this list is renamed with a permanent redirect.",
            });
        });

        api.MapGet("/modlists/{id}", async (
            string id, HttpContext http, ModlistRepository modlists, CancellationToken ct) =>
        {
            var canonical = await modlists.ResolveAliasAsync(id, ct);
            if (canonical is not null)
            {
                // A retired id keeps resolving forever, so old links and exports do not rot.
                return Results.RedirectToRoute(null, new { }, permanent: true,
                    fragment: null) is var _ ? Results.Redirect($"/api/v1/modlists/{canonical}", permanent: true) : Results.NotFound();
            }

            var modlist = await modlists.FindAsync(id, ct);
            if (modlist is null) return Results.NotFound();

            var principal = await http.PrincipalForModlistAsync(modlists, id, ct);

            if (!Permissions.CanViewModlist(principal, modlist.Visibility, modlist.ListingState))
            {
                return Results.NotFound();
            }

            var draft = await modlists.DraftAsync(id, ct);

            return Results.Ok(new
            {
                spec_version = 1,
                id = modlist.Id,
                type = ContentType.ModPack,
                name = modlist.Name,
                @abstract = modlist.Abstract,
                description = modlist.Description,
                license = modlist.License,
                tags = modlist.Tags,
                visibility = modlist.Visibility,
                listing_state = modlist.ListingState,
                draft_revision = modlist.DraftRevision,
                draft = principal?.IsListEditor == true
                    ? draft.Select(d => new { kind = d.Kind, id = d.TargetId, version = d.PinnedVersion, note = d.Note })
                    : null,
            });
        }).RequireRateLimiting("reads");

        api.MapPost("/modlists/{id}/draft/entries", async (
            string id, DraftEntryBody body, HttpContext http,
            ModlistRepository modlists, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModlistAsync(modlists, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.EditModlistDraft)) return Results.Forbid();

            var expected = ReadIfMatch(http);
            if (expected is null) return MissingIfMatch();

            try
            {
                var revision = await modlists.MutateDraftAsync(id, expected.Value,
                    async (connection, transaction, modlistId) =>
                    {
                        var position = await connection.ExecuteScalarAsync<int>(
                            "select coalesce(max(position), -1) + 1 from modlist_draft_entry where modlist_id = @modlistId",
                            new { modlistId }, transaction);

                        await connection.ExecuteAsync("""
                            insert into modlist_draft_entry (modlist_id, entry_kind, target_id, pinned_version, position, note, added_by)
                            values (@modlistId, @kind, @targetId, @version, @position, @note, @addedBy)
                            on conflict (modlist_id, entry_kind, target_id) do update set
                                pinned_version = excluded.pinned_version,
                                note = excluded.note
                            """,
                            new
                            {
                                modlistId,
                                kind = body.Kind,
                                targetId = body.TargetId,
                                version = body.Version,
                                position,
                                note = body.Note,
                                addedBy = principal.AccountId,
                            },
                            transaction);
                    }, ct);

                return Results.Ok(new { draft_revision = revision });
            }
            catch (DraftConflictException conflict)
            {
                return await ConflictWithCurrentStateAsync(modlists, id, conflict, ct);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        api.MapDelete("/modlists/{id}/draft/entries/{kind}/{targetId}", async (
            string id, string kind, string targetId, HttpContext http,
            ModlistRepository modlists, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModlistAsync(modlists, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.EditModlistDraft)) return Results.Forbid();

            var expected = ReadIfMatch(http);
            if (expected is null) return MissingIfMatch();

            try
            {
                var revision = await modlists.MutateDraftAsync(id, expected.Value,
                    async (connection, transaction, modlistId) =>
                        await connection.ExecuteAsync("""
                            delete from modlist_draft_entry
                            where modlist_id = @modlistId and entry_kind = @kind and target_id = @targetId
                            """,
                            new { modlistId, kind, targetId }, transaction),
                    ct);

                return Results.Ok(new { draft_revision = revision });
            }
            catch (DraftConflictException conflict)
            {
                return await ConflictWithCurrentStateAsync(modlists, id, conflict, ct);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        api.MapPost("/modlists/{id}/publish", async (
            string id, PublishBody body, HttpContext http,
            ModlistRepository modlists, Database database, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModlistAsync(modlists, id, ct);
            if (principal is null) return Results.Unauthorized();

            // Editors edit; admins publish. Publishing mints an immutable version other people
            // will install, which is a meaningfully different act.
            if (!Permissions.Allows(principal, Capability.PublishModlistVersion)) return Results.Forbid();

            if (!SemVer.TryParse(body.Version, out var version))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["version"] = ["A modlist version must be valid SemVer 2.0.0."],
                });
            }

            var draft = await modlists.DraftAsync(id, ct);
            var catalogue = await LoadMembersAsync(database, draft, ct);

            var prepared = ModlistPublish.Prepare(draft, catalogue, body.Confirm ?? false);

            if (!prepared.CanPublish)
            {
                return Results.BadRequest(new
                {
                    error = prepared.Issues.Any(i => i.Blocking) ? "cannot_publish" : "confirmation_required",
                    issues = prepared.Issues.Select(i => new
                    {
                        kind = i.Kind.ToString(),
                        blocking = i.Blocking,
                        detail = i.Detail,
                    }),
                });
            }

            try
            {
                var versionId = await modlists.PublishAsync(
                    id, version.Normalised, version.ToSortKey(), body.Changelog,
                    prepared, principal.AccountId, ct);

                return Results.Created($"/api/v1/modlists/{id}/versions/{version.Normalised}", new
                {
                    version = version.Normalised,
                    version_id = versionId,
                    game_min_revision = prepared.GameMinRevision,
                    game_max_revision = prepared.GameMaxRevision,
                    // Exact pins, never ranges: a modlist is a curated, tested set.
                    mods = prepared.Mods.Select(m => new { id = m.Id, version = m.Version }),
                    warnings = prepared.Issues.Where(i => !i.Blocking).Select(i => i.Detail),
                });
            }
            catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
            {
                return Results.Conflict(new
                {
                    error = "version_exists",
                    detail = $"Version {version.Normalised} has already been published. A published version is immutable.",
                });
            }
        });

        api.MapGet("/modlists/{id}/versions/{version}", async (
            string id, string version, HttpContext http,
            ModlistRepository modlists, Database database, CancellationToken ct) =>
        {
            var modlist = await modlists.FindAsync(id, ct);
            if (modlist is null) return Results.NotFound();

            var principal = await http.PrincipalForModlistAsync(modlists, id, ct);
            if (!Permissions.CanViewModlist(principal, modlist.Visibility, modlist.ListingState))
            {
                return Results.NotFound();
            }

            using var connection = await database.OpenAsync(ct);

            var row = await connection.QuerySingleOrDefaultAsync<(long Id, string Version, string? Changelog, int? Min, int? Max, DateTimeOffset PublishedAt)?>("""
                select v.id, v.version, v.changelog, v.game_min_revision, v.game_max_revision, v.published_at
                from modlist_version v
                join modlist m on m.id = v.modlist_id
                where m.id_lower = @id and v.version = @version
                """,
                new { id = id.ToLowerInvariant(), version });

            if (row is null) return Results.NotFound();

            var pins = await connection.QueryAsync<(string EntryKind, string TargetId, string Version)>("""
                select entry_kind, target_id, version from modlist_pin
                where modlist_version_id = @versionId order by position
                """,
                new { versionId = row.Value.Id });

            return Results.Ok(new
            {
                spec_version = 1,
                id = modlist.Id,
                type = ContentType.ModPack,
                version = row.Value.Version,
                released_at = row.Value.PublishedAt,
                changelog = row.Value.Changelog,
                game_min_revision = row.Value.Min,
                game_max_revision = row.Value.Max,
                mods = pins.Where(p => p.EntryKind == "mod").Select(p => new { id = p.TargetId, version = p.Version }),
                vehicles = pins.Where(p => p.EntryKind == "vehicle").Select(p => new { id = p.TargetId, version = p.Version }),
                saves = pins.Where(p => p.EntryKind == "save").Select(p => new { id = p.TargetId, version = p.Version }),
            });
        }).RequireRateLimiting("reads");

        api.MapPost("/modlists/{id}/collaborators", async (
            string id, InviteBody body, HttpContext http,
            ModlistRepository modlists, Database database, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModlistAsync(modlists, id, ct);
            if (principal is null) return Results.Unauthorized();
            if (!Permissions.Allows(principal, Capability.ManageCollaborators)) return Results.Forbid();

            if (body.Role is not (ModlistRole.Admin or ModlistRole.Editor))
            {
                // Ownership transfer is its own operation. Exactly one owner per modlist:
                // co-ownership produces disputes with no tiebreaker.
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["role"] = ["A collaborator may be 'admin' or 'editor'. Use ownership transfer to change owner."],
                });
            }

            using var connection = await database.OpenAsync(ct);

            var accountId = await connection.ExecuteScalarAsync<long?>(
                "select id from account where handle = @handle", new { handle = body.Handle });

            if (accountId is null) return Results.NotFound(new { error = "no_such_account" });

            var inviteId = Guid.CreateVersion7();

            await connection.ExecuteAsync("""
                insert into modlist_invite (id, modlist_id, email_or_handle, role, created_by, expires_at)
                values (@inviteId, (select id from modlist where id_lower = @id), @handle, @role, @createdBy, now() + interval '14 days')
                """,
                new
                {
                    inviteId,
                    id = id.ToLowerInvariant(),
                    handle = body.Handle,
                    role = body.Role,
                    createdBy = principal.AccountId,
                });

            return Results.Ok(new { invite_id = inviteId });
        });

        api.MapGet("/modlists/{id}/activity", async (
            string id, HttpContext http, ModlistRepository modlists, Database database, CancellationToken ct) =>
        {
            var principal = await http.PrincipalForModlistAsync(modlists, id, ct);
            if (principal?.IsListEditor != true) return Results.Forbid();

            using var connection = await database.OpenAsync(ct);

            // What makes a multi-editor list comprehensible when you come back to it.
            var entries = await connection.QueryAsync<(string Kind, string TargetId, string? Version, DateTimeOffset AddedAt, string? Handle)>("""
                select e.entry_kind, e.target_id, e.pinned_version, e.added_at, a.handle
                from modlist_draft_entry e
                left join account a on a.id = e.added_by
                where e.modlist_id = (select id from modlist where id_lower = @id)
                order by e.added_at desc
                limit 100
                """,
                new { id = id.ToLowerInvariant() });

            return Results.Ok(entries.Select(e => new
            {
                kind = e.Kind,
                id = e.TargetId,
                version = e.Version,
                added_at = e.AddedAt,
                by = e.Handle,
            }));
        }).RequireRateLimiting("reads");
    }

    /// <summary>
    /// Optimistic concurrency for draft writes. Two people editing one draft is the normal case,
    /// so a stale write must lose cleanly rather than silently clobbering the other editor.
    /// </summary>
    private static int? ReadIfMatch(HttpContext http)
    {
        var header = http.Request.Headers.IfMatch.ToString().Trim('"');
        return int.TryParse(header, out var revision) ? revision : null;
    }

    private static IResult MissingIfMatch() => Results.BadRequest(new
    {
        error = "if_match_required",
        detail = "Draft writes must carry If-Match with the draft_revision you last read.",
    });

    /// <summary>A 409 carries the current state, not just an error — that is what lets a UI merge.</summary>
    private static async Task<IResult> ConflictWithCurrentStateAsync(
        ModlistRepository modlists, string id, DraftConflictException conflict, CancellationToken ct)
    {
        var draft = await modlists.DraftAsync(id, ct);

        return Results.Json(new
        {
            error = "draft_conflict",
            detail = "Somebody else edited this draft. Merge their change and retry.",
            expected = conflict.Expected,
            current_revision = conflict.Actual,
            current_draft = draft.Select(d => new
            {
                kind = d.Kind,
                id = d.TargetId,
                version = d.PinnedVersion,
                note = d.Note,
            }),
        }, statusCode: StatusCodes.Status409Conflict);
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<MemberRelease>>> LoadMembersAsync(
        Database database, IReadOnlyList<DraftEntry> draft, CancellationToken ct)
    {
        if (draft.Count == 0) return new Dictionary<string, IReadOnlyList<MemberRelease>>();

        var ids = draft.Select(d => d.TargetId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        using var connection = await database.OpenAsync(ct);

        var rows = await connection.QueryAsync<(long Id, string ModId, string Version, int? Min, int? Max, bool Yanked, string ListingState, string Status)>("""
            select r.id, m.id, r.version, r.game_min_revision, r.game_max_revision,
                   (r.yanked_at is not null), m.listing_state, m.status
            from mod_release r
            join mod m on m.id = r.mod_id
            where m.id_lower = any(@ids)
              and r.validation_state in ('passed', 'passed_warnings')
            """,
            new { ids = ids.Select(i => i.ToLowerInvariant()).ToArray() });

        var releases = rows.ToList();
        var releaseIds = releases.Select(r => r.Id).ToArray();

        var assets = await connection.QueryAsync<(long ReleaseId, string AssetId)>(
            "select release_id, asset_id from release_asset_id where release_id = any(@releaseIds)",
            new { releaseIds });

        var dependencies = await connection.QueryAsync<(long ReleaseId, string DepId)>("""
            select release_id, dep_id from release_dependency
            where release_id = any(@releaseIds) and kind = 'required' and dep_id is not null
            """,
            new { releaseIds });

        var assetsByRelease = assets.GroupBy(a => a.ReleaseId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(a => a.AssetId)]);

        var dependenciesByRelease = dependencies.GroupBy(d => d.ReleaseId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(d => d.DepId)]);

        return releases
            .GroupBy(r => r.ModId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<MemberRelease>)
                [
                    .. g.Select(r => new MemberRelease
                    {
                        ModId = r.ModId,
                        Version = r.Version,
                        GameMinRevision = r.Min,
                        GameMaxRevision = r.Max,
                        Yanked = r.Yanked,
                        ListingState = r.ListingState,
                        Status = r.Status,
                        AssetIds = assetsByRelease.GetValueOrDefault(r.Id, []),
                        RequiredDependencies = dependenciesByRelease.GetValueOrDefault(r.Id, []),
                    }),
                ],
                StringComparer.OrdinalIgnoreCase);
    }
}
