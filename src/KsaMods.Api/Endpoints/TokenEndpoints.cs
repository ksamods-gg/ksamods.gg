using KsaMods.Api.Auth;

namespace KsaMods.Api.Endpoints;

public sealed record CreateTokenBody(string Name, string? Kind, int? ExpiresInDays);

/// <summary>
/// Managing your own API tokens (backend.md §4.1).
///
/// <para>Every route here needs a <b>session</b>, never a token. A credential that can mint its
/// own successors cannot be revoked: you take one away and it has already made another. So the
/// browser is the only place tokens are born, and the only place they die.</para>
/// </summary>
public static class TokenEndpoints
{
    /// <summary>Enough to be useful, few enough that a compromised account is still auditable.</summary>
    private const int MaxPerAccount = 20;

    public static void MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/me/tokens").RequireRateLimiting("writes");

        api.MapGet("/", async (HttpContext http, TokenStore tokens, CancellationToken ct) =>
        {
            // Session only, deliberately: listing your credentials through one of them turns a
            // leaked read token into a map of everything else the account holds.
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            var issued = await tokens.ListAsync(user.AccountId, ct);

            return Results.Ok(issued.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                kind = t.Kind,
                // The whole secret is unrecoverable; this is what tells two rows apart.
                prefix = t.Prefix,
                created_at = t.CreatedAt,
                expires_at = t.ExpiresAt,
                last_used_at = t.LastUsedAt,
            }));
        });

        api.MapPost("/", async (
            CreateTokenBody body, HttpContext http, TokenStore tokens, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            var name = body.Name?.Trim() ?? "";

            if (name.Length is < 1 or > 64)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    // Named, because a list of identical unnamed tokens is a list nobody can
                    // safely revoke from.
                    ["name"] = ["Give it a name you will recognise in six months. Up to 64 characters."],
                });
            }

            var kind = body.Kind is "application" ? "application" : "personal";

            if ((await tokens.ListAsync(user.AccountId, ct)).Count >= MaxPerAccount)
            {
                return Results.Conflict(new
                {
                    error = "too_many_tokens",
                    detail = $"You already have {MaxPerAccount} tokens. Revoke one you no longer use.",
                });
            }

            DateTimeOffset? expiresAt = body.ExpiresInDays is { } days and > 0
                ? DateTimeOffset.UtcNow.AddDays(Math.Min(days, 365 * 2))
                : null;

            var issued = await tokens.IssueAsync(user.AccountId, name, kind, expiresAt, ct);

            return Results.Ok(new
            {
                id = issued.Id,
                name,
                kind,
                // The only time this exists outside the holder's hands. Nothing stores it, so
                // there is no "show again" and the copy affordance has to be here.
                token = issued.Secret,
                expires_at = expiresAt,
                note = kind == "application"
                    ? "Send it as `Authorization: Bearer <token>`. It identifies your software and "
                      + "raises the read limit. It cannot write, and it cannot see anybody's drafts."
                    : "Send it as `Authorization: Bearer <token>`. It reads as you - including your "
                      + "own drafts and failed validation reports - and raises the read limit. It "
                      + "cannot write; publishing stays in the browser.",
            });
        });

        api.MapDelete("/{id:long}", async (
            long id, HttpContext http, TokenStore tokens, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.Unauthorized();

            return await tokens.RevokeAsync(user.AccountId, id, ct)
                ? Results.NoContent()
                : Results.NotFound();
        });
    }
}
