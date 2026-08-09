using KsaMods.Api.Data;
using KsaMods.Api.Domain;

namespace KsaMods.Api.Auth;

/// <summary>
/// Resolves the session cookie and any bearer token onto the request, once, before any endpoint
/// runs.
///
/// <para><b>They land in different slots, and that is the authorisation model.</b> Only a session
/// is written to the slot <see cref="User"/> reads from, so every write path in the API - all of
/// which ask for <c>http.User()</c> - refuses a token without a single one of them knowing that
/// tokens exist. A read path that wants to serve an owner their own drafts asks
/// <see cref="Caller"/> instead, which accepts either.
///
/// <para>Written this way round on purpose. The alternative - one slot plus a flag each handler
/// remembers to check - is a rule with one chance to be right per endpoint and forty chances to be
/// forgotten. This one fails closed by construction.</para>
/// </summary>
public static class CurrentUserMiddleware
{
    private const string ItemKey = "ksamods.user";
    private const string TokenKey = "ksamods.token";

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

            if (BearerToken(context) is { } presented)
            {
                var tokens = context.RequestServices.GetRequiredService<TokenStore>();

                // Never into ItemKey. A token that reached the session slot would satisfy every
                // write check in the API at once.
                context.Items[TokenKey] = await tokens.ResolveAsync(presented, context.RequestAborted);
            }

            await next();
        });

    /// <summary>
    /// The signed-in user, or null. A bearer token never answers this, which is what makes every
    /// write endpoint token-proof without knowing tokens exist.
    /// </summary>
    public static AuthenticatedUser? User(this HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as AuthenticatedUser : null;

    public static TokenPrincipal? Token(this HttpContext context) =>
        context.Items.TryGetValue(TokenKey, out var value) ? value as TokenPrincipal : null;

    /// <summary>
    /// Who is asking, by session or by token, for read paths that serve an owner their own
    /// non-public data. An application token identifies software rather than a person, so it
    /// deliberately does not answer here: it buys quota, not sight of somebody's drafts.
    /// </summary>
    public static (long AccountId, string SiteRole)? Caller(this HttpContext context)
    {
        if (context.User() is { } user) return (user.AccountId, user.SiteRole);

        return context.Token() is { IsApplication: false } token
            ? (token.AccountId, token.SiteRole)
            : null;
    }

    /// <summary>
    /// The rate-limit partition: the token when one was presented, then the session, then the
    /// address. A key is how somebody earns a higher ceiling, so it has to be what the bucket is
    /// keyed on - otherwise one office shares one quota and a key buys nothing.
    /// </summary>
    public static string RateLimitPartition(this HttpContext context) =>
        context.Token() is { } token ? $"token:{token.TokenId}"
        : context.User() is { } user ? $"account:{user.AccountId}"
        : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    public static bool HasKey(this HttpContext context) => context.Token() is not null;

    /// <summary>
    /// From the Authorization header only, never a query string: URLs reach proxy logs, browser
    /// history and referrer headers, and a credential that leaks by being copied out of an address
    /// bar is one nobody knows to revoke.
    /// </summary>
    private static string? BearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

        var value = header["Bearer ".Length..].Trim();

        return TokenStore.LooksLikeToken(value) ? value : null;
    }

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

    /// <summary>
    /// The same principal, for <b>read paths only</b>, accepting a personal token as well as a
    /// session. This is what lets a script holding a token see its own drafts and failed
    /// validation reports.
    ///
    /// <para>Separate from <see cref="PrincipalForModAsync"/> rather than a flag on it, because
    /// that method is what every write endpoint asks for permission with. One method serving both
    /// would mean a token satisfying <c>Capability.DeleteMod</c> the day somebody widened it, and
    /// the widening would look harmless in review. Two methods make the mistake impossible to
    /// make by accident: a write path would have to call the one named for reading.</para>
    /// </summary>
    public static async Task<Principal?> ReadPrincipalForModAsync(
        this HttpContext context, ModRepository mods, string modId, CancellationToken ct)
    {
        if (context.Caller() is not { } caller) return null;

        return new Principal
        {
            AccountId = caller.AccountId,
            SiteRole = caller.SiteRole,
            ModRole = await mods.RoleOfAsync(modId, caller.AccountId, ct),
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
