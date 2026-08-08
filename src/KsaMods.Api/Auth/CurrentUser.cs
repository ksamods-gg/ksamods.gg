using KsaMods.Api.Data;
using KsaMods.Api.Domain;

namespace KsaMods.Api.Auth;

/// <summary>Resolves the session cookie onto the request, once, before any endpoint runs.</summary>
public static class CurrentUserMiddleware
{
    private const string ItemKey = "ksamods.user";

    public static IApplicationBuilder UseCurrentUser(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var options = context.RequestServices.GetRequiredService<SiteSessionOptions>();

            if (context.Request.Cookies.TryGetValue(options.CookieName, out var raw) &&
                Guid.TryParse(raw, out var sessionId))
            {
                var sessions = context.RequestServices.GetRequiredService<SessionStore>();
                var user = await sessions.ResolveAsync(sessionId, context.RequestAborted);

                if (user is not null)
                {
                    context.Items[ItemKey] = user;
                    // Sliding expiry, refreshed at most once a day so an active session does not
                    // mean a write on every request.
                    await sessions.TouchAsync(sessionId, context.RequestAborted);
                }
                else
                {
                    context.Response.Cookies.Delete(options.CookieName);
                }
            }

            await next();
        });

    public static AuthenticatedUser? User(this HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as AuthenticatedUser : null;

    public static AuthenticatedUser Require(this HttpContext context) =>
        context.User() ?? throw new UnauthorisedException();

    /// <summary>
    /// Builds the principal for a mod, loading the caller's role. Returned even for anonymous
    /// callers so read paths can ask the same questions as write paths.
    /// </summary>
    public static async Task<Principal?> PrincipalForModAsync(
        this HttpContext context, ModRepository mods, string modId, CancellationToken ct)
    {
        var user = context.User();
        if (user is null) return null;

        return new Principal
        {
            AccountId = user.AccountId,
            SiteRole = user.SiteRole,
            ModRole = await mods.RoleOfAsync(modId, user.AccountId, ct),
        };
    }

    public static async Task<Principal?> PrincipalForModlistAsync(
        this HttpContext context, ModlistRepository modlists, string modlistId, CancellationToken ct)
    {
        var user = context.User();
        if (user is null) return null;

        return new Principal
        {
            AccountId = user.AccountId,
            SiteRole = user.SiteRole,
            ModlistRole = await modlists.RoleOfAsync(modlistId, user.AccountId, ct),
        };
    }
}

public sealed class UnauthorisedException : Exception;
