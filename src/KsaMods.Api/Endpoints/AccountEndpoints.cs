using Dapper;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Metadata;

namespace KsaMods.Api.Endpoints;

public sealed record UpdateProfileBody(string? DisplayName, string? Handle, string? ForumsUrl);

/// <summary>
/// Managing your own account (backend.md §4).
///
/// <para>Everything here is scoped to the caller by construction: no endpoint takes an account id,
/// so there is no path where a missing authorisation check lets one person edit another's profile.
/// The only account these can touch is the one the session names.</para>
/// </summary>
public static class AccountEndpoints
{
    /// <summary>Handles follow the same charset rules as content ids, minus the dots.</summary>
    private const int MinHandle = 2;
    private const int MaxHandle = 24;

    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/me").RequireRateLimiting("writes");

        api.MapGet("/profile", async (HttpContext http, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            using var connection = await database.OpenAsync(ct);

            var account = await connection.QuerySingleOrDefaultAsync<AccountProfileRow>("""
                select handle as Handle, display_name as DisplayName, avatar_url as AvatarUrl,
                       forums_url as ForumsUrl, site_role as SiteRole, created_at as CreatedAt
                from account where id = @id and deleted_at is null
                """,
                new { id = user.AccountId });

            if (account is null) return Results.Unauthorized();

            var identities = await connection.QueryAsync<(string Provider, DateTimeOffset LinkedAt)>(
                "select provider, linked_at from oauth_identity where account_id = @id order by provider",
                new { id = user.AccountId });

            var mods = await connection.QueryAsync<(string Id, string Name, string Role)>("""
                select m.id, m.name, mm.role
                from mod_maintainer mm join mod m on m.id = mm.mod_id
                where mm.account_id = @id
                order by m.id_lower
                """,
                new { id = user.AccountId });

            var modlists = await connection.QueryAsync<(string Id, string Name, string Role, string Visibility)>("""
                select ml.id, ml.name, mc.role, ml.visibility
                from modlist_collaborator mc join modlist ml on ml.id = mc.modlist_id
                where mc.account_id = @id and mc.accepted_at is not null
                order by ml.id_lower
                """,
                new { id = user.AccountId });

            return Results.Ok(new
            {
                handle = account.Handle,
                display_name = account.DisplayName,
                avatar_url = account.AvatarUrl,
                forums_url = account.ForumsUrl,
                site_role = account.SiteRole,
                created_at = account.CreatedAt,
                identities = identities.Select(i => new { provider = i.Provider, linked_at = i.LinkedAt }),
                mods = mods.Select(m => new { id = m.Id, name = m.Name, role = m.Role }),
                modlists = modlists.Select(m => new
                {
                    id = m.Id, name = m.Name, role = m.Role, visibility = m.Visibility,
                }),
            });
        });

        api.MapPatch("/profile", async (
            UpdateProfileBody body, HttpContext http, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            var errors = new Dictionary<string, string[]>();

            if (body.DisplayName is { } displayName && string.IsNullOrWhiteSpace(displayName))
            {
                errors["displayName"] = ["A display name cannot be blank."];
            }

            if (body.Handle is { } handle && ValidateHandle(handle) is { } handleError)
            {
                errors["handle"] = [handleError];
            }

            // Only https, and only somewhere else — the same rule the banner URL follows, for the
            // same reason: a link we render must not be able to point back into this site or load
            // over plain http on an https page.
            if (!string.IsNullOrWhiteSpace(body.ForumsUrl)
                && !body.ForumsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                errors["forumsUrl"] = ["Must be an https:// link."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            using var connection = await database.OpenAsync(ct);

            try
            {
                var updated = await connection.ExecuteAsync("""
                    update account set
                        display_name = coalesce(@displayName, display_name),
                        handle       = coalesce(@handle, handle),
                        forums_url   = case when @clearForums then null
                                            else coalesce(@forumsUrl, forums_url) end
                    where id = @id and deleted_at is null
                    """,
                    new
                    {
                        id = user.AccountId,
                        displayName = string.IsNullOrWhiteSpace(body.DisplayName) ? null : body.DisplayName.Trim(),
                        handle = string.IsNullOrWhiteSpace(body.Handle) ? null : body.Handle.Trim(),
                        forumsUrl = string.IsNullOrWhiteSpace(body.ForumsUrl) ? null : body.ForumsUrl.Trim(),
                        clearForums = body.ForumsUrl is { Length: 0 },
                    });

                return updated == 0 ? Results.Unauthorized() : Results.NoContent();
            }
            catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
            {
                // handle is citext, so this catches a clash that differs only in case — which a
                // `where handle = @candidate` pre-check would miss entirely. Ask the index.
                return Results.Conflict(new
                {
                    error = "handle_taken",
                    detail = "Somebody already has that name. Handles ignore capitalisation, so "
                           + "'Alice' and 'alice' count as the same one.",
                });
            }
        });

        api.MapGet("/sessions", async (HttpContext http, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            using var connection = await database.OpenAsync(ct);

            var rows = await connection.QueryAsync<(Guid Id, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string? UserAgent)>("""
                select id, issued_at, expires_at, user_agent
                from session
                where account_id = @id and revoked_at is null and expires_at > now()
                order by issued_at desc
                """,
                new { id = user.AccountId });

            return Results.Ok(rows.Select(r => new
            {
                id = r.Id,
                issued_at = r.IssuedAt,
                expires_at = r.ExpiresAt,
                // Never the raw address: the row stores only a hash of it, and the point of this
                // list is "is anything here not me", which the device string answers.
                user_agent = r.UserAgent,
                current = r.Id == user.SessionId,
            }));
        });

        api.MapPost("/sessions/revoke-others", async (
            HttpContext http, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            using var connection = await database.OpenAsync(ct);

            // Everything except this one, so signing other devices out does not sign you out of
            // the page you are doing it from.
            var revoked = await connection.ExecuteAsync("""
                update session set revoked_at = now()
                where account_id = @id and id <> @current and revoked_at is null
                """,
                new { id = user.AccountId, current = user.SessionId });

            return Results.Ok(new { revoked });
        });

        api.MapDelete("/identities/{provider}", async (
            string provider, HttpContext http, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            var (connection, transaction) = await database.BeginTransactionAsync(ct);

            await using (connection)
            await using (transaction)
            {
                var remaining = await connection.ExecuteScalarAsync<int>(
                    "select count(*) from oauth_identity where account_id = @id",
                    new { id = user.AccountId }, transaction);

                if (remaining <= 1)
                {
                    // Unlinking the last one leaves an account nobody can ever sign into again.
                    // There is no password to fall back on and no email to recover through.
                    return Results.Conflict(new
                    {
                        error = "last_identity",
                        detail = "This is the only way you can sign in. Link another provider "
                               + "first, or delete the account if that is what you meant.",
                    });
                }

                var removed = await connection.ExecuteAsync(
                    "delete from oauth_identity where account_id = @id and provider = @provider",
                    new { id = user.AccountId, provider }, transaction);

                if (removed == 0) return Results.NotFound();

                if (provider == "github")
                {
                    await connection.ExecuteAsync(
                        "update account set github_login = null where id = @id",
                        new { id = user.AccountId }, transaction);
                }
                else if (provider == "discord")
                {
                    await connection.ExecuteAsync(
                        "update account set discord_id = null where id = @id",
                        new { id = user.AccountId }, transaction);
                }

                await transaction.CommitAsync(ct);
                return Results.NoContent();
            }
        });

        api.MapPost("/delete", async (HttpContext http, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            var (connection, transaction) = await database.BeginTransactionAsync(ct);

            await using (connection)
            await using (transaction)
            {
                // Anonymise rather than remove (§4.3). Deleting the row would break every modlist
                // pinning this person's mods and every dependency naming them, so published
                // metadata stays resolvable and the identity behind it does not.
                var tombstone = $"deleted{Guid.NewGuid():N}"[..20];

                await connection.ExecuteAsync("""
                    update account set
                        handle        = @tombstone,
                        display_name  = 'Deleted account',
                        github_login  = null,
                        discord_id    = null,
                        avatar_url    = null,
                        forums_url    = null,
                        deleted_at    = now()
                    where id = @id and deleted_at is null
                    """,
                    new { id = user.AccountId, tombstone }, transaction);

                // No provider can sign back into it.
                await connection.ExecuteAsync(
                    "delete from oauth_identity where account_id = @id",
                    new { id = user.AccountId }, transaction);

                await connection.ExecuteAsync(
                    "update session set revoked_at = now() where account_id = @id and revoked_at is null",
                    new { id = user.AccountId }, transaction);

                await transaction.CommitAsync(ct);
            }

            return Results.Ok(new
            {
                deleted = true,
                note = "Your mods and modlists are still published. They are no longer attributed "
                     + "to a named account.",
            });
        });
    }

    /// <summary>
    /// Handle rules, kept deliberately narrower than a display name.
    ///
    /// <para>A handle is an identifier people type at each other — in a collaborator invite, for
    /// instance — so it takes the same ASCII-only discipline as a content id and for the same
    /// reasons (spec §3). Anything expressive belongs in the display name, which has no rules.</para>
    /// </summary>
    private static string? ValidateHandle(string handle)
    {
        if (handle.Length is < MinHandle or > MaxHandle)
        {
            return $"Must be {MinHandle} to {MaxHandle} characters.";
        }

        foreach (var c in handle)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
            {
                return "Only letters, digits, '-' and '_'.";
            }
        }

        if (!char.IsAsciiLetterOrDigit(handle[0]) || !char.IsAsciiLetterOrDigit(handle[^1]))
        {
            return "Must start and end with a letter or digit.";
        }

        // A handle that reads like a content id invites confusion in invites and mentions.
        if (ContentId.IsReserved(handle)) return "That name is reserved.";

        return null;
    }

    private sealed record AccountProfileRow
    {
        public string Handle { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string? AvatarUrl { get; init; }
        public string? ForumsUrl { get; init; }
        public string SiteRole { get; init; } = "user";
        public DateTimeOffset CreatedAt { get; init; }
    }
}
