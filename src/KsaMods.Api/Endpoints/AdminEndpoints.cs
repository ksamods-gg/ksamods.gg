using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Api.Domain;
using Npgsql;

namespace KsaMods.Api.Endpoints;

public sealed record ResolveReportBody(string State, string Rationale);
public sealed record ListingStateBody(string State, string Rationale);
public sealed record SuspendBody(bool Suspended, string Rationale);
public sealed record SiteRoleBody(string Role, string Rationale);
public sealed record ClearReviewBody(string? Notes);

/// <summary>Requests permitted per window, per partition.</summary>
public sealed record RateLimitBody(int Reads, int Writes, int Webhooks, int WindowSeconds);

/// <summary>An empty message means no banner, which is how one is taken down.</summary>
public sealed record NoticeBody(
    string? Message, string Variant, string? LinkText, string? LinkHref, bool Dismissible);

/// <summary>What is being reported, why, and anything the reporter wants to add.</summary>
public sealed record FileReportBody(string SubjectKind, string SubjectId, string Category, string? Body);

/// <summary>
/// The moderation surface (backend.md §13).
///
/// <para>Three rules hold across every endpoint here, and they are most of the design:</para>
///
/// <para><b>Every action writes to the moderation log in the same transaction as its effect.</b>
/// A delisting that lands without its log row - or a log row without its delisting - is worse than
/// neither, because the log is the only account of what staff did and it is exported so it outlives
/// this service (§13.4).</para>
///
/// <para><b>Anything done to someone carries a rationale.</b> Withdrawing a listing, closing a
/// report, suspending an account, taking a role back - all of them owe the person on the other end
/// an explanation, and a log full of blank reasons is the same as no log. Granting a role is the
/// exception: nobody is owed a justification for being given something, and requiring one there
/// buys "helping out" rather than information.</para>
///
/// <para><b>Withdrawing is delisting, never deleting.</b> Nothing in here removes a listing.
/// Modlists pin releases and dependency graphs name ids, so a hole in the graph is a worse failure
/// than a listing marked withdrawn - which stays resolvable by id (§13.3).</para>
/// </summary>
public static class AdminEndpoints
{
    /// <summary>
    /// Filing a report. The other half of the moderation queue.
    ///
    /// <para>Outside the /admin group on purpose: every other endpoint in this file requires
    /// Moderate, and this one is the only way anything ever reaches the queue those endpoints
    /// read. Without it the reports screen is a window onto a table nothing can write to.</para>
    /// </summary>
    /// <summary>
    /// The site notice, read and written.
    ///
    /// <para>The read is public and unauthenticated because the banner is on every page including
    /// pages nobody is signed in for. The write needs Moderate, and lives here with the rest of
    /// the moderation surface rather than in configuration, so putting up an outage message is
    /// something a moderator does at the moment it is needed rather than a deploy.</para>
    /// </summary>
    public static void MapNoticeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/notice", async (Database database, CancellationToken ct) =>
        {
            using var connection = await database.OpenAsync(ct);

            var notice = await connection.QuerySingleOrDefaultAsync<NoticeRow>("""
                select message, variant, link_text as LinkText, link_href as LinkHref,
                       dismissible, updated_at as UpdatedAt
                from site_notice where id = 1
                """);

            return Results.Ok(new
            {
                message = notice?.Message ?? "",
                variant = notice?.Variant ?? "info",
                link_text = notice?.LinkText,
                link_href = notice?.LinkHref,
                dismissible = notice?.Dismissible ?? true,
                updated_at = notice?.UpdatedAt,
            });
        }).RequireRateLimiting("reads");

        app.MapPut("/api/v1/admin/notice", async (
            NoticeBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            var errors = new Dictionary<string, string[]>();

            if (body.Variant is not ("info" or "warning" or "error"))
            {
                errors["variant"] = ["Pick info, warning or error."];
            }

            if (body.Message is { Length: > 400 })
            {
                errors["message"] = ["Keep it to 400 characters or fewer. It sits on every page."];
            }

            var hasText = !string.IsNullOrWhiteSpace(body.LinkText);
            var hasHref = !string.IsNullOrWhiteSpace(body.LinkHref);

            if (hasText != hasHref)
            {
                errors["linkHref"] = ["A link needs both its words and its address, or neither."];
            }

            if (hasHref && !body.LinkHref!.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                errors["linkHref"] = ["Must be an https:// link."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            using var connection = await database.OpenAsync(ct);

            await connection.ExecuteAsync("""
                update site_notice set
                    message     = @message,
                    variant     = @variant,
                    link_text   = @linkText,
                    link_href   = @linkHref,
                    dismissible = @dismissible,
                    updated_at  = now(),
                    updated_by  = @by
                where id = 1
                """,
                new
                {
                    message = (body.Message ?? "").Trim(),
                    variant = body.Variant,
                    linkText = hasText ? body.LinkText!.Trim() : null,
                    linkHref = hasHref ? body.LinkHref!.Trim() : null,
                    dismissible = body.Dismissible,
                    by = http.User()?.AccountId,
                });

            return Results.NoContent();
        }).RequireRateLimiting("writes");
    }

    /// <summary>
    /// Reading and lowering the request budgets.
    ///
    /// <para>Admin rather than moderator. A limit is not a moderation decision, and setting one
    /// wrongly takes the site off the air for everybody rather than acting on one person.</para>
    /// </summary>
    public static void MapRateLimitEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/admin").RequireRateLimiting("writes");

        api.MapGet("/rate-limits", (HttpContext http, RateLimits limits) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            return Results.Ok(new
            {
                reads = limits.Reads,
                writes = limits.Writes,
                webhooks = limits.Webhooks,
                window_seconds = (int)limits.Window.TotalSeconds,
                changed_at = limits.ChangedAt,
                minimum = RateLimits.Minimum,
                maximum = RateLimits.Maximum,
            });
        });

        api.MapPut("/rate-limits", (RateLimitBody body, HttpContext http, RateLimits limits, ILoggerFactory logging) =>
        {
            // Changing what everybody is allowed to do belongs with the people who can hand out
            // roles, not with everybody who can withdraw a listing.
            if (Deny(http, Capability.ManageSiteRoles) is { } denied) return denied;

            if (!limits.Set(body.Reads, body.Writes, body.Webhooks, body.WindowSeconds, out var error))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reads"] = [error!],
                });
            }

            // Logged rather than written to the moderation log: that log is about what staff did
            // to people, and this is an operational dial. It still needs to be findable afterwards
            // when somebody asks why everything started returning 429.
            logging.CreateLogger("RateLimits").LogWarning(
                "Rate limits changed by {Account}: reads {Reads}, writes {Writes}, webhooks {Webhooks} per {Window}s.",
                http.User()?.Handle, body.Reads, body.Writes, body.Webhooks, body.WindowSeconds);

            return Results.NoContent();
        });
    }

    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireRateLimiting("writes");

        api.MapPost("/reports", async (
            FileReportBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            // Signed in, so a report is attributable and a person who files nonsense repeatedly
            // can be stopped. The reporter column is nullable for account deletion, not to let
            // strangers fill the queue anonymously.
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            var errors = new Dictionary<string, string[]>();

            if (body.SubjectKind is not ("mod" or "release" or "modlist" or "account"))
            {
                errors["subjectKind"] = ["Not something that can be reported."];
            }

            if (body.Category is not ("malware" or "stolen" or "broken" or "other"))
            {
                errors["category"] = ["Pick one of the listed reasons."];
            }

            if (string.IsNullOrWhiteSpace(body.SubjectId))
            {
                errors["subjectId"] = ["Missing what is being reported."];
            }

            // Long enough to explain, short enough that the queue stays readable.
            if (body.Body is { Length: > 2000 })
            {
                errors["body"] = ["Keep it to 2000 characters or fewer."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            using var connection = await database.OpenAsync(ct);

            // One open report per person per subject. Somebody pressing the button twice should
            // not put the same complaint in front of a moderator twice, and it stops the queue
            // being floodable by one account.
            var already = await connection.ExecuteScalarAsync<bool>("""
                select exists (
                    select 1 from report
                    where reporter = @reporter and subject_kind = @kind and subject_id = @id
                      and state = 'open')
                """,
                new { reporter = user.AccountId, kind = body.SubjectKind, id = body.SubjectId });

            if (already)
            {
                return Results.Conflict(new
                {
                    error = "already_reported",
                    detail = "You've already reported this. A moderator will get to it.",
                });
            }

            await connection.ExecuteAsync("""
                insert into report (subject_kind, subject_id, category, reporter, body)
                values (@kind, @id, @category, @reporter, @body)
                """,
                new
                {
                    kind = body.SubjectKind,
                    id = body.SubjectId,
                    category = body.Category,
                    reporter = user.AccountId,
                    body = string.IsNullOrWhiteSpace(body.Body) ? null : body.Body.Trim(),
                });

            return Results.Accepted();
        });
    }

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/admin").RequireRateLimiting("writes");

        // ── overview ──────────────────────────────────────────────────────

        api.MapGet("/overview", async (HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            var counts = await connection.QuerySingleAsync<OverviewRow>($"""
                select
                  (select count(*) from report where state = 'open')                            as OpenReports,
                  (select count(*) from mod_release where availability = 'quarantined')         as Quarantined,
                  (select count(*) from mod_release r where {NeedsReview})                      as NeedsReview,
                  (select count(*) from job where state = 'dead')                               as DeadJobs,
                  (select count(*) from tag where state = 'proposed')                            as PendingTags,
                  (select count(*) from mod where listing_state in ('delisted', 'taken_down'))  as Withdrawn,
                  (select count(*) from account where suspended_at is not null)                 as Suspended,
                  (select count(*) from account where deleted_at is null)                       as Accounts,
                  (select count(*) from mod)                                                    as Mods,
                  (select count(*) from mod_release)                                            as Releases
                """);

            return Results.Ok(new
            {
                open_reports = counts.OpenReports,
                quarantined = counts.Quarantined,
                needs_review = counts.NeedsReview,
                dead_jobs = counts.DeadJobs,
                pending_tags = counts.PendingTags,
                withdrawn = counts.Withdrawn,
                suspended = counts.Suspended,
                accounts = counts.Accounts,
                mods = counts.Mods,
                releases = counts.Releases,
            });
        });

        // ── reports ───────────────────────────────────────────────────────

        api.MapGet("/reports", async (
            HttpContext http, Database database, string? state, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            var rows = await connection.QueryAsync<ReportRow>("""
                select r.id, r.subject_kind as SubjectKind, r.subject_id as SubjectId,
                       r.category, r.body, r.state, r.created_at as CreatedAt,
                       r.resolved_at as ResolvedAt,
                       reporter.handle::text as ReporterHandle,
                       resolver.handle::text as ResolvedBy
                from report r
                left join account reporter on reporter.id = r.reporter
                left join account resolver on resolver.id = r.resolved_by
                where @state is null or r.state = @state
                order by (r.state <> 'open'), r.created_at desc
                limit 200
                """,
                new { state });

            return Results.Ok(rows.Select(r => new
            {
                id = r.Id,
                subject_kind = r.SubjectKind,
                subject_id = r.SubjectId,
                category = r.Category,
                body = r.Body,
                state = r.State,
                created_at = r.CreatedAt,
                resolved_at = r.ResolvedAt,
                reporter_handle = r.ReporterHandle,
                resolved_by = r.ResolvedBy,
            }));
        });

        api.MapPost("/reports/{id:long}/resolve", async (
            long id, ResolveReportBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;
            if (Blank(body.Rationale) is { } missing) return missing;

            if (body.State is not ("triaged" or "actioned" or "dismissed"))
            {
                return Invalid("state", "Must be triaged, actioned or dismissed.");
            }

            var actor = http.Require().AccountId;
            var (connection, transaction) = await database.BeginTransactionAsync(ct);

            await using (connection)
            await using (transaction)
            {
                var subject = await connection.QuerySingleOrDefaultAsync<ReportSubjectRow>(
                    "select subject_kind as SubjectKind, subject_id as SubjectId from report where id = @id",
                    new { id }, transaction);

                if (subject is null) return Results.NotFound();

                await connection.ExecuteAsync("""
                    update report set state = @state, resolved_by = @actor, resolved_at = now()
                    where id = @id
                    """,
                    new { id, state = body.State, actor }, transaction);

                await LogAsync(connection, transaction, actor, $"report_{body.State}",
                    subject.SubjectKind, subject.SubjectId, body.Rationale);

                await transaction.CommitAsync(ct);
            }

            return Results.NoContent();
        });

        // ── review queue ──────────────────────────────────────────────────

        api.MapGet("/reviews", async (HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            // The queue is derived, not stored (see migration 0004). Each flag comes back with the
            // row so the list can say *why* something is here rather than only that it is.
            var rows = await connection.QueryAsync<ReviewRow>($"""
                select r.id, r.mod_id as ModId, r.version, r.released_at as ReleasedAt,
                       r.availability, r.validation_state as ValidationState, m.name as ModName,
                       exists (select 1 from release_assembly a where a.release_id = r.id) as ShipsCode,
                       exists (select 1 from release_console c where c.release_id = r.id)  as RunsConsole,
                       (r.availability = 'quarantined')                                    as Quarantined,
                       (select count(*) from release_finding f
                        where f.release_id = r.id and f.severity = 'warning')              as Warnings,
                       (select count(*) from report p
                        where p.state = 'open' and p.subject_kind = 'mod'
                          and p.subject_id::citext = m.id_lower)                           as OpenReports
                from mod_release r
                join mod m on m.id = r.mod_id
                where {NeedsReview}
                order by (r.availability = 'quarantined') desc, r.released_at desc
                limit 100
                """);

            return Results.Ok(rows.Select(r => new
            {
                id = r.Id,
                mod_id = r.ModId,
                mod_name = r.ModName,
                version = r.Version,
                released_at = r.ReleasedAt,
                availability = r.Availability,
                validation_state = r.ValidationState,
                ships_code = r.ShipsCode,
                runs_console = r.RunsConsole,
                quarantined = r.Quarantined,
                warnings = r.Warnings,
                open_reports = r.OpenReports,
            }));
        });

        api.MapPost("/reviews/{releaseId:long}/clear", async (
            long releaseId, ClearReviewBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            var actor = http.Require().AccountId;
            var (connection, transaction) = await database.BeginTransactionAsync(ct);

            await using (connection)
            await using (transaction)
            {
                var release = await connection.QuerySingleOrDefaultAsync<ReleaseKeyRow>(
                    "select mod_id as ModId, version from mod_release where id = @releaseId",
                    new { releaseId }, transaction);

                if (release is null) return Results.NotFound();

                await connection.ExecuteAsync("""
                    insert into release_review (release_id, reviewed_by, outcome, notes)
                    values (@releaseId, @actor, 'cleared', @notes)
                    on conflict (release_id) do nothing
                    """,
                    new { releaseId, actor, notes = body.Notes }, transaction);

                // Logged even though nothing happened to the release: "somebody looked and left it
                // alone" is exactly the fact a later argument turns on.
                await LogAsync(connection, transaction, actor, "review_cleared", "release",
                    $"{release.ModId}@{release.Version}",
                    string.IsNullOrWhiteSpace(body.Notes) ? "Reviewed; nothing to act on." : body.Notes);

                await transaction.CommitAsync(ct);
            }

            return Results.NoContent();
        });

        // ── listings ──────────────────────────────────────────────────────

        api.MapGet("/listings", async (
            HttpContext http, Database database, string? q, string? state, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            var rows = await connection.QueryAsync<ListingRow>("""
                select m.id, m.name, m.listing_state as ListingState, m.status,
                       m.updated_at as UpdatedAt, a.handle::text as OwnerHandle,
                       (select count(*) from mod_release r where r.mod_id = m.id) as Releases,
                       (select count(*) from report p
                        where p.state = 'open' and p.subject_kind = 'mod'
                          and p.subject_id::citext = m.id_lower)                   as OpenReports
                from mod m
                left join mod_maintainer mm on mm.mod_id = m.id and mm.role = 'owner'
                left join account a on a.id = mm.account_id
                where (@q is null or m.name ilike '%' || @q || '%' or m.id ilike '%' || @q || '%')
                  and (@state is null or m.listing_state = @state)
                order by m.updated_at desc
                limit 100
                """,
                new { q, state });

            return Results.Ok(rows.Select(r => new
            {
                id = r.Id,
                name = r.Name,
                listing_state = r.ListingState,
                status = r.Status,
                updated_at = r.UpdatedAt,
                owner_handle = r.OwnerHandle,
                releases = r.Releases,
                open_reports = r.OpenReports,
            }));
        });

        api.MapPost("/listings/{id}/state", async (
            string id, ListingStateBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;
            if (Blank(body.Rationale) is { } missing) return missing;

            // The index's vocabulary only. `deprecated` is the author's word and `yanked` belongs to
            // a single release; a moderator writing either would be putting words in the author's
            // mouth, which is the reason the two vocabularies are kept apart (§13.1).
            if (body.State is not ("listed" or "unlisted" or "delisted" or "taken_down"))
            {
                return Invalid("state", "Must be listed, unlisted, delisted or taken_down.");
            }

            var actor = http.Require().AccountId;
            var (connection, transaction) = await database.BeginTransactionAsync(ct);

            await using (connection)
            await using (transaction)
            {
                // Resolve to canonical casing first: the log entry has to name the listing the way
                // the listing names itself, not the way the URL happened to be typed.
                var canonical = await connection.ExecuteScalarAsync<string?>(
                    "select id from mod where id_lower = @id::citext", new { id }, transaction);

                if (canonical is null) return Results.NotFound();

                await connection.ExecuteAsync(
                    "update mod set listing_state = @state, updated_at = now() where id = @canonical",
                    new { canonical, state = body.State }, transaction);

                await LogAsync(connection, transaction, actor, $"listing_{body.State}",
                    "mod", canonical, body.Rationale);

                await transaction.CommitAsync(ct);
            }

            return Results.NoContent();
        });

        // ── accounts ──────────────────────────────────────────────────────

        api.MapGet("/accounts", async (
            HttpContext http, Database database, string? q, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            var rows = await connection.QueryAsync<AccountRow>("""
                select a.id, a.handle::text as Handle, a.display_name as DisplayName,
                       a.site_role as SiteRole, a.created_at as CreatedAt,
                       a.suspended_at as SuspendedAt, a.deleted_at as DeletedAt,
                       (select count(*) from mod_maintainer mm where mm.account_id = a.id) as Mods,
                       (select count(*) from session s
                        where s.account_id = a.id and s.revoked_at is null
                          and s.expires_at > now())                                        as Sessions
                from account a
                where @q is null or a.handle ilike '%' || @q || '%'
                                 or a.display_name ilike '%' || @q || '%'
                order by (a.site_role <> 'user') desc, a.created_at desc
                limit 100
                """,
                new { q });

            return Results.Ok(rows.Select(r => new
            {
                id = r.Id,
                handle = r.Handle,
                display_name = r.DisplayName,
                site_role = r.SiteRole,
                created_at = r.CreatedAt,
                suspended_at = r.SuspendedAt,
                deleted_at = r.DeletedAt,
                mods = r.Mods,
                sessions = r.Sessions,
            }));
        });

        api.MapPost("/accounts/{id:long}/suspend", async (
            long id, SuspendBody body, HttpContext http, Database database,
            SessionStore sessions, CancellationToken ct) =>
        {
            if (Deny(http, Capability.SuspendAccount) is { } denied) return denied;
            if (Blank(body.Rationale) is { } missing) return missing;

            var principal = Current(http)!;

            if (principal.AccountId == id)
            {
                // Locking yourself out is never the intent, and undoing it needs somebody else.
                return Results.Conflict(new
                {
                    error = "self_suspend",
                    detail = "You cannot suspend your own account.",
                });
            }

            var (connection, transaction) = await database.BeginTransactionAsync(ct);

            await using (connection)
            await using (transaction)
            {
                var target = await connection.QuerySingleOrDefaultAsync<TargetAccountRow>(
                    "select handle::text as Handle, site_role as SiteRole from account "
                    + "where id = @id and deleted_at is null",
                    new { id }, transaction);

                if (target is null) return Results.NotFound();

                // A moderator must not be able to disable an admin, or the level below can switch
                // off the level above and the hierarchy is decoration.
                if (target.SiteRole == SiteRole.Admin && !principal.IsAdmin)
                {
                    return Forbidden("Only an admin can suspend another admin.");
                }

                await connection.ExecuteAsync(
                    "update account set suspended_at = @when where id = @id",
                    new { id, when = body.Suspended ? DateTimeOffset.UtcNow : (DateTimeOffset?)null },
                    transaction);

                await LogAsync(connection, transaction, principal.AccountId,
                    body.Suspended ? "account_suspended" : "account_reinstated",
                    "account", target.Handle, body.Rationale);

                await transaction.CommitAsync(ct);
            }

            // Suspension has to bite now rather than whenever the cookie happens to expire.
            if (body.Suspended) await sessions.RevokeAllAsync(id, ct);

            return Results.NoContent();
        });

        api.MapPost("/accounts/{id:long}/role", async (
            long id, SiteRoleBody body, HttpContext http, Database database,
            SessionStore sessions, CancellationToken ct) =>
        {
            if (Deny(http, Capability.ManageSiteRoles) is { } denied) return denied;

            if (body.Role is not (SiteRole.User or SiteRole.Moderator or SiteRole.Admin))
            {
                return Invalid("role", "Must be user, moderator or admin.");
            }

            var principal = Current(http)!;

            if (principal.AccountId == id)
            {
                // Demoting yourself could leave the site with no admin at all, and promoting
                // yourself is the thing this capability exists to prevent.
                return Results.Conflict(new
                {
                    error = "self_role_change",
                    detail = "You cannot change your own role. Ask another admin.",
                });
            }

            var (connection, transaction) = await database.BeginTransactionAsync(ct);

            await using (connection)
            await using (transaction)
            {
                var target = await connection.QuerySingleOrDefaultAsync<TargetAccountRow>(
                    "select handle::text as Handle, site_role as SiteRole from account "
                    + "where id = @id and deleted_at is null",
                    new { id }, transaction);

                if (target is null) return Results.NotFound();

                // A reason is owed to someone who is losing something. Taking powers away is a
                // thing done *to* a person, and they will want to know why; handing them out is
                // not, and demanding a sentence for it only fills the log with "helping out".
                // The entry itself is written either way - who promoted whom is the audit trail,
                // and that is never optional.
                var lowering = Rank(body.Role) < Rank(target.SiteRole);

                if (lowering && Blank(body.Rationale) is { } missing) return missing;

                await connection.ExecuteAsync(
                    "update account set site_role = @role where id = @id",
                    new { id, role = body.Role }, transaction);

                var rationale = string.IsNullOrWhiteSpace(body.Rationale)
                    ? $"Given the {body.Role} role."
                    : body.Rationale;

                await LogAsync(connection, transaction, principal.AccountId,
                    $"role_{body.Role}", "account", target.Handle, rationale);

                await transaction.CommitAsync(ct);
            }

            // Sessions carry the role, so leaving them alive would delay the change until the last
            // one expired - and in the demotion direction that delay is the whole problem.
            await sessions.RevokeAllAsync(id, ct);

            return Results.NoContent();
        });

        // ── moderation log ────────────────────────────────────────────────

        api.MapGet("/log", async (
            HttpContext http, Database database, string? subjectId, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            var rows = await connection.QueryAsync<ModerationRow>("""
                select l.id, l.action, l.subject_kind as SubjectKind, l.subject_id as SubjectId,
                       l.rationale, l.public as IsPublic, l.created_at as CreatedAt,
                       l.supersedes, a.handle::text as ActorHandle
                from moderation_action l
                left join account a on a.id = l.actor
                where @subjectId is null or l.subject_id::citext = @subjectId::citext
                order by l.id desc
                limit 200
                """,
                new { subjectId });

            return Results.Ok(rows.Select(r => new
            {
                id = r.Id,
                action = r.Action,
                subject_kind = r.SubjectKind,
                subject_id = r.SubjectId,
                rationale = r.Rationale,
                @public = r.IsPublic,
                created_at = r.CreatedAt,
                supersedes = r.Supersedes,
                actor_handle = r.ActorHandle,
            }));
        });

        // ── jobs ──────────────────────────────────────────────────────────

        api.MapGet("/jobs", async (HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            // Finished jobs are left out. The states worth a page are the ones somebody may have to
            // act on, and a list that is mostly successes buries them.
            var rows = await connection.QueryAsync<JobRow>("""
                select id, kind, state, attempts, run_after as RunAfter,
                       last_error as LastError, created_at as CreatedAt
                from job
                where state in ('dead', 'failed', 'running', 'queued')
                order by case state when 'dead' then 0 when 'failed' then 1
                                    when 'running' then 2 else 3 end,
                         created_at desc
                limit 100
                """);

            return Results.Ok(rows.Select(r => new
            {
                id = r.Id,
                kind = r.Kind,
                state = r.State,
                attempts = r.Attempts,
                run_after = r.RunAfter,
                last_error = r.LastError,
                created_at = r.CreatedAt,
            }));
        });

        api.MapPost("/jobs/{id:long}/retry", async (
            long id, HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            // Attempts reset with it: a job retried after its cause was fixed deserves the full
            // backoff budget rather than dying again on the first hiccup.
            var revived = await connection.ExecuteAsync("""
                update job set state = 'queued', attempts = 0, run_after = now(),
                               locked_by = null, locked_at = null
                where id = @id and state in ('dead', 'failed')
                """,
                new { id });

            return revived == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    /// <summary>
    /// What puts a release in the review queue, written once because the overview badge and the
    /// queue itself have to agree - a "3" above a list of five is a bug report waiting to happen.
    /// Assumes the release is aliased <c>r</c>, which both callers do.
    ///
    /// <para>The conditions: it ships a managed assembly, it runs console commands at a game hook,
    /// or validation quarantined it. Each is a reason for a person to look before the listing is
    /// treated as ordinary. Anything already reviewed drops out.</para>
    /// </summary>
    private const string NeedsReview = """
        r.validation_state in ('passed', 'passed_warnings')
          and not exists (select 1 from release_review v where v.release_id = r.id)
          and (exists (select 1 from release_assembly a where a.release_id = r.id)
               or exists (select 1 from release_console c where c.release_id = r.id)
               or r.availability = 'quarantined')
        """;

    /// <summary>
    /// Writes the audit row. Always takes the caller's transaction, so the record and the thing it
    /// records commit together or not at all (§13.4).
    /// </summary>
    private static Task LogAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        long actor, string action, string subjectKind, string subjectId, string rationale) =>
        connection.ExecuteAsync("""
            insert into moderation_action (actor, action, subject_kind, subject_id, rationale)
            values (@actor, @action, @subjectKind, @subjectId, @rationale)
            """,
            new { actor, action, subjectKind, subjectId, rationale }, transaction);

    private static Principal? Current(HttpContext http)
    {
        var user = http.User();

        return user is null ? null : new Principal
        {
            AccountId = user.AccountId,
            SiteRole = user.SiteRole,
        };
    }

    /// <summary>
    /// The gate in front of every endpoint here. Returns the result to send, or null to carry on -
    /// so the check is one line at the top of each handler and its absence is visible.
    /// </summary>
    private static IResult? Deny(HttpContext http, Capability capability)
    {
        var principal = Current(http);

        if (principal is null) return Results.Unauthorized();

        // 404 rather than 403: /admin should not confirm to a stranger that it is there.
        return Permissions.Allows(principal, capability) ? null : Results.NotFound();
    }

    private static IResult? Blank(string? rationale) =>
        string.IsNullOrWhiteSpace(rationale)
            ? Invalid("rationale", "Say why. This goes in the moderation log.")
            : null;

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>
    /// Orders the roles so "is this a demotion" is a comparison rather than a list of pairs.
    /// </summary>
    private static int Rank(string role) => role switch
    {
        SiteRole.Admin => 2,
        SiteRole.Moderator => 1,
        _ => 0,
    };

    private static IResult Forbidden(string detail) =>
        Results.Json(new { error = "forbidden", detail }, statusCode: StatusCodes.Status403Forbidden);

    // ── row shapes ────────────────────────────────────────────────────────

    private sealed record NoticeRow
    {
        public string Message { get; init; } = "";
        public string Variant { get; init; } = "info";
        public string? LinkText { get; init; }
        public string? LinkHref { get; init; }
        public bool Dismissible { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed record OverviewRow
    {
        public int OpenReports { get; init; }
        public int Quarantined { get; init; }
        public int NeedsReview { get; init; }
        public int DeadJobs { get; init; }
        public int PendingTags { get; init; }
        public int Withdrawn { get; init; }
        public int Suspended { get; init; }
        public int Accounts { get; init; }
        public int Mods { get; init; }
        public int Releases { get; init; }
    }

    private sealed record ReportRow
    {
        public long Id { get; init; }
        public string SubjectKind { get; init; } = "";
        public string SubjectId { get; init; } = "";
        public string Category { get; init; } = "";
        public string? Body { get; init; }
        public string State { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ResolvedAt { get; init; }
        public string? ReporterHandle { get; init; }
        public string? ResolvedBy { get; init; }
    }

    private sealed record ReportSubjectRow
    {
        public string SubjectKind { get; init; } = "";
        public string SubjectId { get; init; } = "";
    }

    private sealed record ReleaseKeyRow
    {
        public string ModId { get; init; } = "";
        public string Version { get; init; } = "";
    }

    private sealed record ReviewRow
    {
        public long Id { get; init; }
        public string ModId { get; init; } = "";
        public string ModName { get; init; } = "";
        public string Version { get; init; } = "";
        public DateTimeOffset ReleasedAt { get; init; }
        public string Availability { get; init; } = "";
        public string ValidationState { get; init; } = "";
        public bool ShipsCode { get; init; }
        public bool RunsConsole { get; init; }
        public bool Quarantined { get; init; }
        public int Warnings { get; init; }
        public int OpenReports { get; init; }
    }

    private sealed record ListingRow
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string ListingState { get; init; } = "";
        public string Status { get; init; } = "";
        public DateTimeOffset UpdatedAt { get; init; }
        public string? OwnerHandle { get; init; }
        public int Releases { get; init; }
        public int OpenReports { get; init; }
    }

    private sealed record AccountRow
    {
        public long Id { get; init; }
        public string Handle { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string SiteRole { get; init; } = "user";
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? SuspendedAt { get; init; }
        public DateTimeOffset? DeletedAt { get; init; }
        public int Mods { get; init; }
        public int Sessions { get; init; }
    }

    private sealed record TargetAccountRow
    {
        public string Handle { get; init; } = "";
        public string SiteRole { get; init; } = "user";
    }

    private sealed record ModerationRow
    {
        public long Id { get; init; }
        public string Action { get; init; } = "";
        public string SubjectKind { get; init; } = "";
        public string SubjectId { get; init; } = "";
        public string Rationale { get; init; } = "";
        public bool IsPublic { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public long? Supersedes { get; init; }
        public string? ActorHandle { get; init; }
    }

    private sealed record JobRow
    {
        public long Id { get; init; }
        public string Kind { get; init; } = "";
        public string State { get; init; } = "";
        public int Attempts { get; init; }
        public DateTimeOffset RunAfter { get; init; }
        public string? LastError { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
