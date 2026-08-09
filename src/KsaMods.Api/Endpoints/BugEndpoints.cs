using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Api.Domain;

namespace KsaMods.Api.Endpoints;

public sealed record FileBugBody(string Summary, string? Detail, string? Page);

/// <summary>The outcome, and what the reporter will be told about it.</summary>
public sealed record ResolveBugBody(string State, string Resolution);

/// <summary>
/// Bugs in the site, and telling the person who reported one what happened to it.
///
/// <para>Separate from <c>report</c>, which is content moderation. A bug goes to whoever maintains
/// the site rather than to whoever judges listings, and mixing them would put "the search box is
/// broken" in the same queue as takedown requests.</para>
/// </summary>
public static class BugEndpoints
{
    public static void MapBugEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireRateLimiting("writes");

        api.MapPost("/bugs", async (
            FileBugBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            // Signed in, because the whole point is being able to tell them the answer. An
            // anonymous bug report is a message in a bottle.
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            var summary = (body.Summary ?? "").Trim();

            if (summary.Length is < 3 or > 200)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["summary"] = ["Say what went wrong, in a sentence. Between 3 and 200 characters."],
                });
            }

            if (body.Detail is { Length: > 4000 })
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["detail"] = ["Keep it to 4000 characters or fewer."],
                });
            }

            using var connection = await database.OpenAsync(ct);

            var id = await connection.ExecuteScalarAsync<long>("""
                insert into bug_report (reporter, summary, detail, page)
                values (@reporter, @summary, @detail, @page)
                returning id
                """,
                new
                {
                    reporter = user.AccountId,
                    summary,
                    detail = string.IsNullOrWhiteSpace(body.Detail) ? null : body.Detail.Trim(),

                    // Trimmed to a path. A full URL would carry whatever query string they had,
                    // which is a way to record more about somebody than they meant to send.
                    page = Trim(body.Page),
                });

            return Results.Accepted(value: new { id });
        });

        // What the signed-in visitor is owed: outcomes they have not been shown yet.
        //
        // Read on every page render for a signed-in visitor, so it answers with the smallest
        // thing that will do and leans on a partial index to answer "nothing" instantly.
        api.MapGet("/me/bugs/unseen", async (
            HttpContext http, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Ok(Array.Empty<object>());

            using var connection = await database.OpenAsync(ct);

            var rows = await connection.QueryAsync<UnseenBugRow>("""
                select id, summary, state, resolution, resolved_at as ResolvedAt
                from bug_report
                where reporter = @reporter and state <> 'open' and seen_at is null
                order by resolved_at
                limit 5
                """,
                new { reporter = user.AccountId });

            return Results.Ok(rows.Select(r => new
            {
                id = r.Id,
                summary = r.Summary,
                state = r.State,
                resolution = r.Resolution,
                resolved_at = r.ResolvedAt,
            }));
        }).RequireRateLimiting("reads");

        api.MapPost("/me/bugs/{id:long}/seen", async (
            long id, HttpContext http, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            using var connection = await database.OpenAsync(ct);

            // Scoped to the caller in the statement itself, so there is no path where a wrong id
            // acknowledges somebody else's news and they never see it.
            await connection.ExecuteAsync("""
                update bug_report set seen_at = now()
                where id = @id and reporter = @reporter and seen_at is null
                """,
                new { id, reporter = user.AccountId });

            return Results.NoContent();
        });

        // ── staff ──

        var admin = app.MapGroup("/api/v1/admin").RequireRateLimiting("writes");

        admin.MapGet("/bugs", async (
            HttpContext http, Database database, string? state, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            var rows = await connection.QueryAsync<BugRow>("""
                select b.id, b.summary, b.detail, b.page, b.state, b.resolution,
                       b.created_at as CreatedAt, b.resolved_at as ResolvedAt,
                       b.seen_at as SeenAt,
                       reporter.handle::text as ReporterHandle,
                       resolver.handle::text as ResolvedBy
                from bug_report b
                left join account reporter on reporter.id = b.reporter
                left join account resolver on resolver.id = b.resolved_by
                where @state is null or b.state = @state
                order by (b.state <> 'open'), b.created_at
                limit 200
                """,
                new { state });

            return Results.Ok(rows.Select(b => new
            {
                id = b.Id,
                summary = b.Summary,
                detail = b.Detail,
                page = b.Page,
                state = b.State,
                resolution = b.Resolution,
                created_at = b.CreatedAt,
                resolved_at = b.ResolvedAt,
                reporter_handle = b.ReporterHandle,
                resolved_by = b.ResolvedBy,

                // Whether the reporter has been shown the outcome yet. Worth surfacing: it is the
                // difference between "dealt with" and "dealt with and they know".
                told = b.SeenAt is not null,
            }));
        });

        admin.MapPost("/bugs/{id:long}/resolve", async (
            long id, ResolveBugBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http, Capability.Moderate) is { } denied) return denied;

            if (body.State is not ("fixed" or "known" or "declined" or "duplicate"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["state"] = ["Pick fixed, known, declined or duplicate."],
                });
            }

            // Required, because this is the sentence the reporter reads. Closing one with a blank
            // answer is the same as never replying, which is the thing this feature exists to
            // stop.
            if (string.IsNullOrWhiteSpace(body.Resolution))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["resolution"] = ["Say what happened. The reporter is shown this."],
                });
            }

            using var connection = await database.OpenAsync(ct);

            var updated = await connection.ExecuteAsync("""
                update bug_report
                set state = @state, resolution = @resolution,
                    resolved_at = now(), resolved_by = @by,
                    -- Cleared so a re-resolved report is shown again: if the answer changed,
                    -- the person who reported it should hear the new one.
                    seen_at = null
                where id = @id
                """,
                new
                {
                    id,
                    state = body.State,
                    resolution = body.Resolution.Trim(),
                    by = http.User()?.AccountId,
                });

            return updated == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    /// <summary>Path only, and only if it looks like one. Never a full URL with its query string.</summary>
    private static string? Trim(string? page)
    {
        if (string.IsNullOrWhiteSpace(page)) return null;

        var path = page.Trim();

        if (Uri.TryCreate(path, UriKind.Absolute, out var url)) path = url.AbsolutePath;

        var query = path.IndexOfAny(['?', '#']);
        if (query >= 0) path = path[..query];

        return path.Length is 0 or > 300 ? null : path;
    }

    private static IResult? Deny(HttpContext http, Capability capability)
    {
        var user = http.User();
        if (user is null) return Results.Unauthorized();

        var principal = new Principal { AccountId = user.AccountId, SiteRole = user.SiteRole };

        // 404 rather than 403, matching the rest of /admin: it should not confirm to a stranger
        // that it is there.
        return Permissions.Allows(principal, capability) ? null : Results.NotFound();
    }

    private sealed record UnseenBugRow
    {
        public long Id { get; init; }
        public string Summary { get; init; } = "";
        public string State { get; init; } = "";
        public string? Resolution { get; init; }
        public DateTimeOffset? ResolvedAt { get; init; }
    }

    private sealed record BugRow
    {
        public long Id { get; init; }
        public string Summary { get; init; } = "";
        public string? Detail { get; init; }
        public string? Page { get; init; }
        public string State { get; init; } = "";
        public string? Resolution { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ResolvedAt { get; init; }
        public DateTimeOffset? SeenAt { get; init; }
        public string? ReporterHandle { get; init; }
        public string? ResolvedBy { get; init; }
    }
}
