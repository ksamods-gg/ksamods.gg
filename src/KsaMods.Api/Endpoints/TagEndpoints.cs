using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Api.Domain;

namespace KsaMods.Api.Endpoints;

public sealed record ProposeTagBody(string Slug, string? Reason);
public sealed record ReviewTagBody(string? Label, string? Description, string? Note);
public sealed record CreateTagBody(string Slug, string? Label, string? Description);

/// <summary>
/// The tag vocabulary, from both ends: what anybody may read and suggest, and what an admin may
/// decide (RFC 0031, backend.md §3).
///
/// <para>Two audiences, one list. An author picking tags needs the approved set and a way to say
/// "the thing I made has no word here yet"; an admin needs the queue that produces. Keeping the
/// suggestion in the same place as the vocabulary is what stops the vocabulary ossifying - a
/// curated list nobody can add to is just a shorter free-form list.</para>
/// </summary>
public static class TagEndpoints
{
    public static void MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireRateLimiting("writes");

        // ── everyone ──────────────────────────────────────────────────────

        // Public and unauthenticated: it is the filter vocabulary for browse, not a secret.
        api.MapGet("/tags", async (TagVocabulary tags, CancellationToken ct) =>
        {
            var approved = await tags.ApprovedAsync(ct);

            return Results.Ok(new
            {
                spec_version = 1,
                items = approved.Select(t => new
                {
                    slug = t.Slug,
                    label = t.Label,
                    description = t.Description,
                }),
            });
        });

        api.MapPost("/tags", async (
            ProposeTagBody body, HttpContext http, TagVocabulary tags, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            var slug = TagVocabulary.Normalise(body.Slug ?? "");

            if (TagVocabulary.Malformed(slug) is { } problem)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["slug"] = [problem],
                });
            }

            var (outcome, note) = await tags.ProposeAsync(slug, body.Reason?.Trim(), user.AccountId, ct);

            return outcome switch
            {
                TagProposal.Proposed => Results.Ok(new
                {
                    slug,
                    state = "proposed",
                    note = "Suggested. An admin decides whether it joins the list; until then you "
                         + "cannot tag anything with it.",
                }),

                // Not an error. Somebody asking for a tag that already exists has been served: the
                // thing they wanted is available.
                TagProposal.AlreadyApproved => Results.Ok(new
                {
                    slug,
                    state = "approved",
                    note = "That tag already exists - you can use it now.",
                }),

                TagProposal.AlreadyPending => Results.Ok(new
                {
                    slug,
                    state = "proposed",
                    note = "Somebody already suggested it. It is waiting on an admin.",
                }),

                _ => Results.Conflict(new
                {
                    error = "tag_rejected",
                    slug,
                    detail = string.IsNullOrWhiteSpace(note)
                        ? "That tag was suggested before and turned down."
                        : $"That tag was suggested before and turned down: {note}",
                }),
            };
        });

        // ── admins ────────────────────────────────────────────────────────

        var admin = app.MapGroup("/api/v1/admin").RequireRateLimiting("writes");

        admin.MapGet("/tags", async (
            HttpContext http, Database database, string? state, CancellationToken ct) =>
        {
            if (Deny(http) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            // Use counts come back with the list because they are what makes a decision informed:
            // approving a tag nothing will carry, or retiring one on forty listings, are different
            // acts and the queue should not make them look alike.
            var rows = await connection.QueryAsync<TagRow>("""
                select t.slug, t.label, t.description, t.state, t.reason,
                       t.created_at as CreatedAt, t.reviewed_at as ReviewedAt,
                       t.review_note as ReviewNote,
                       a.handle::text as ProposerHandle,
                       (select count(*) from mod m where t.slug = any(m.tags)) as Uses
                from tag t
                left join account a on a.id = t.proposed_by
                where (@state is null or t.state = @state)
                order by (t.state = 'proposed') desc, t.slug
                """,
                new { state });

            return Results.Ok(rows.Select(Present));
        });

        // Tags carried by listings that the vocabulary does not know. Because the column is
        // deliberately permissive - the format allows anything lowercase, and ingested listings
        // arrive with their own words - this is the curation backlog rather than an error list.
        admin.MapGet("/tags/unknown", async (
            HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http) is { } denied) return denied;

            using var connection = await database.OpenAsync(ct);

            var rows = await connection.QueryAsync<(string Tag, int Uses)>("""
                select lower(t) as Tag, count(*) as Uses
                from mod m, unnest(m.tags) as t
                where not exists (select 1 from tag where slug = lower(t))
                group by lower(t)
                order by count(*) desc, lower(t)
                """);

            return Results.Ok(rows.Select(r => new { tag = r.Tag, uses = r.Uses }));
        });

        admin.MapPost("/tags", async (
            CreateTagBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http) is { } denied) return denied;

            var slug = TagVocabulary.Normalise(body.Slug ?? "");

            if (TagVocabulary.Malformed(slug) is { } problem)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["slug"] = [problem] });
            }

            var actor = http.Require().AccountId;

            using var connection = await database.OpenAsync(ct);

            // An admin adding a tag is approving it in one step; there is nobody left to ask.
            // `do update` rather than `do nothing`, so adding a tag that was previously rejected
            // is how an admin changes their mind.
            await connection.ExecuteAsync("""
                insert into tag (slug, label, description, state, reviewed_by, reviewed_at)
                values (@slug, @label, @description, 'approved', @actor, now())
                on conflict (slug) do update set
                    label = excluded.label,
                    description = excluded.description,
                    state = 'approved',
                    reviewed_by = excluded.reviewed_by,
                    reviewed_at = now(),
                    review_note = null
                """,
                new
                {
                    slug,
                    label = string.IsNullOrWhiteSpace(body.Label) ? slug : body.Label.Trim(),
                    description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
                    actor,
                });

            return Results.Ok(new { slug, state = "approved" });
        });

        admin.MapPost("/tags/{slug}/approve", async (
            string slug, ReviewTagBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http) is { } denied) return denied;

            var actor = http.Require().AccountId;
            var normalised = TagVocabulary.Normalise(slug);

            using var connection = await database.OpenAsync(ct);

            // Approving is also the moment to tidy the wording: somebody suggesting a tag proposes
            // a slug, and the label and description are the site's to write.
            var updated = await connection.ExecuteAsync("""
                update tag set state = 'approved',
                               label = coalesce(@label, label),
                               description = coalesce(@description, description),
                               reviewed_by = @actor, reviewed_at = now(), review_note = null
                where slug = @normalised
                """,
                new
                {
                    normalised,
                    actor,
                    label = string.IsNullOrWhiteSpace(body?.Label) ? null : body.Label.Trim(),
                    description = string.IsNullOrWhiteSpace(body?.Description) ? null : body.Description.Trim(),
                });

            return updated == 0 ? Results.NotFound() : Results.Ok(new { slug = normalised, state = "approved" });
        });

        admin.MapPost("/tags/{slug}/reject", async (
            string slug, ReviewTagBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            if (Deny(http) is { } denied) return denied;

            if (string.IsNullOrWhiteSpace(body?.Note))
            {
                // The note is the whole value of a rejection: without it the same tag gets
                // suggested again next month by somebody who cannot know it was considered.
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["note"] = ["Say why. The person who suggested it sees this."],
                });
            }

            var actor = http.Require().AccountId;
            var normalised = TagVocabulary.Normalise(slug);

            var (connection, transaction) = await database.BeginTransactionAsync(ct);

            await using (connection)
            await using (transaction)
            {
                var uses = await connection.ExecuteScalarAsync<int>(
                    "select count(*) from mod where @normalised = any(tags)",
                    new { normalised }, transaction);

                var updated = await connection.ExecuteAsync("""
                    update tag set state = 'rejected', reviewed_by = @actor, reviewed_at = now(),
                                   review_note = @note
                    where slug = @normalised
                    """,
                    new { normalised, actor, note = body.Note.Trim() }, transaction);

                if (updated == 0) return Results.NotFound();

                // Retiring a tag that listings already carry has to take it off them in the same
                // transaction. Leaving it would make the vocabulary a lie - the tag would keep
                // filtering and keep appearing on cards while the list said it was not a tag.
                if (uses > 0)
                {
                    await connection.ExecuteAsync(
                        "update mod set tags = array_remove(tags, @normalised), updated_at = now() "
                        + "where @normalised = any(tags)",
                        new { normalised }, transaction);

                    await connection.ExecuteAsync("""
                        insert into moderation_action (actor, action, subject_kind, subject_id, rationale)
                        values (@actor, 'tag_retired', 'tag', @normalised, @rationale)
                        """,
                        new
                        {
                            actor,
                            normalised,
                            rationale = $"Removed from {uses} listing(s). {body.Note.Trim()}",
                        },
                        transaction);
                }

                await transaction.CommitAsync(ct);

                return Results.Ok(new { slug = normalised, state = "rejected", removed_from = uses });
            }
        });
    }

    private static object Present(TagRow t) => new
    {
        slug = t.Slug,
        label = t.Label,
        description = t.Description,
        state = t.State,
        reason = t.Reason,
        proposer_handle = t.ProposerHandle,
        review_note = t.ReviewNote,
        created_at = t.CreatedAt,
        reviewed_at = t.ReviewedAt,
        uses = t.Uses,
    };

    /// <summary>404 rather than 403, matching the rest of /admin: it does not confirm it exists.</summary>
    private static IResult? Deny(HttpContext http)
    {
        var user = http.User();
        if (user is null) return Results.Unauthorized();

        var principal = new Principal { AccountId = user.AccountId, SiteRole = user.SiteRole };

        return Permissions.Allows(principal, Capability.ManageTags) ? null : Results.NotFound();
    }
}
