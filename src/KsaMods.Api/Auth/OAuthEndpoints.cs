using System.Net.Http.Headers;
using System.Text.Json;
using KsaMods.Api.Data;
using Microsoft.AspNetCore.WebUtilities;

namespace KsaMods.Api.Auth;

public sealed record OAuthProviderOptions
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string AuthorizeEndpoint { get; init; }
    public required string TokenEndpoint { get; init; }
    public required string UserEndpoint { get; init; }
    public required string Scope { get; init; }
}

/// <summary>
/// Sign-in with GitHub or Discord (backend.md §4.1).
///
/// <para>Hand-rolled rather than using the framework handlers, because the flow here is small and
/// the session it produces is our own table rather than a cookie principal - going through the
/// authentication middleware would mean translating between two session models for no gain.</para>
/// </summary>
public static class OAuthEndpoints
{
    private const string StateCookie = "ksamods_oauth_state";

    /// <summary>Round-trip markers for what the provider redirect was started for.</summary>
    private const string SignInIntent = "signin";
    private const string LinkIntent = "link";

    /// <summary>
    /// The site's public base URL, e.g. <c>https://ksamods.gg</c>.
    ///
    /// <para><b>Configured, not derived from the request.</b> The API sits behind two proxies -
    /// Coolify's, then the frontend's - so <c>Request.Host</c> inside the container is
    /// <c>api:8080</c>, and a redirect_uri built from it sends users to a hostname that only
    /// exists on a Docker network. Forwarded headers can carry the real host, but the redirect_uri
    /// must match what is registered with the provider <i>byte for byte</i>, and staking that on a
    /// header surviving two hops intact is a bad trade for one setting.</para>
    /// </summary>
    public sealed record SiteOptions
    {
        public string? PublicBaseUrl { get; init; }

        /// <summary>Normalised, with any trailing slash removed and a scheme guaranteed.</summary>
        public string? Normalised
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PublicBaseUrl)) return null;

                var value = PublicBaseUrl.Trim().TrimEnd('/');

                // Coolify's SERVICE_FQDN_* is sometimes a bare host and sometimes a full URL;
                // accept either rather than making the operator care which.
                if (!value.Contains("://", StringComparison.Ordinal)) value = $"https://{value}";

                return value;
            }
        }
    }

    public static void MapOAuth(
        this IEndpointRouteBuilder app,
        IReadOnlyDictionary<string, OAuthProviderOptions> providers,
        SiteSessionOptions sessionOptions,
        SiteOptions siteOptions)
    {
        // Which providers actually have credentials. The frontend asks this so it can hide a
        // sign-in button that could only ever 404 - an operator who has not set the credentials
        // gets a working read-only site, not a broken button.
        //
        // Under /api/v1 rather than /auth: this is data about the flow, not a step in it.
        app.MapGet("/api/v1/auth/providers", () => Results.Ok(new
        {
            providers = providers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
        }));

        // Who the caller is, or 204 for nobody.
        //
        // The frontend needs this to keep people out of forms they cannot submit. Without it the
        // only way to discover that a session is missing is to post the form and read a 401,
        // which means filling in a page of fields to be told the answer was "sign in first".
        //
        // 204 rather than 401 for the anonymous case on purpose: not being signed in is a normal
        // answer to this question, not a failure, and 401 here would make every anonymous page
        // render log an authentication error that nothing went wrong to cause.
        app.MapGet("/api/v1/me", async (HttpContext http, Database database, CancellationToken ct) =>
        {
            var user = http.User();
            if (user is null) return Results.NoContent();

            // The session carries the handle and role, but not the avatar or display name, and the
            // header wants a face rather than a username. One indexed lookup by primary key.
            using var connection = await database.OpenAsync(ct);

            var row = await Dapper.SqlMapper.QuerySingleOrDefaultAsync<(string? DisplayName, string? AvatarUrl)?>(
                connection,
                "select display_name, avatar_url from account where id = @id",
                new { id = user.AccountId });

            return Results.Ok(new
            {
                id = user.AccountId,
                handle = user.Handle,
                site_role = user.SiteRole,
                display_name = row?.DisplayName ?? user.Handle,
                avatar_url = row?.AvatarUrl,
            });
        });

        app.MapGet("/auth/{provider}/start", (
            string provider, HttpContext http, string? returnTo, string? intent) =>
        {
            if (!providers.TryGetValue(provider, out var options)) return NotConfigured(provider);
            if (PublicUrlMissing(siteOptions, http) is { } misconfigured) return misconfigured;

            // Linking needs a session to attach to. Checked here as well as in the callback so the
            // answer arrives before the user is sent off to a provider to no purpose.
            var linking = string.Equals(intent, LinkIntent, StringComparison.Ordinal);
            if (linking && http.User() is null) return Results.Unauthorized();

            // CSRF protection for the callback. Bound to the browser via a cookie so a state
            // value alone is not enough to complete somebody else's sign-in.
            var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

            // The intent rides in the cookie rather than the returnTo or a query parameter, so a
            // crafted callback URL cannot turn a sign-in into a link against a session it does
            // not control. returnTo goes last because it is the only part that can contain a '|'.
            var cookieValue = $"{state}|{(linking ? LinkIntent : SignInIntent)}|{returnTo ?? "/"}";

            http.Response.Cookies.Append(StateCookie, cookieValue, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10),
                Path = "/",
            });

            var redirect = CallbackUrl(siteOptions, http, provider);
            var url = QueryHelpers.AddQueryString(options.AuthorizeEndpoint, new Dictionary<string, string?>
            {
                ["client_id"] = options.ClientId,
                ["redirect_uri"] = redirect,
                ["response_type"] = "code",
                ["scope"] = options.Scope,
                ["state"] = state,
            });

            return Results.Redirect(url);
        });

        app.MapGet("/auth/{provider}/callback", async (
            string provider,
            string? code,
            string? state,
            HttpContext http,
            IHttpClientFactory clients,
            AccountStore accounts,
            SessionStore sessions,
            CancellationToken ct) =>
        {
            if (!providers.TryGetValue(provider, out var options)) return NotConfigured(provider);
            if (PublicUrlMissing(siteOptions, http) is { } misconfigured) return misconfigured;
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)) return Results.BadRequest();

            if (!http.Request.Cookies.TryGetValue(StateCookie, out var stored)) return Results.BadRequest();
            http.Response.Cookies.Delete(StateCookie);

            var parts = stored.Split('|', 3);
            // Constant-time: the state is a secret and a timing oracle on it is a real, if
            // fiddly, CSRF path.
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(parts[0]),
                    System.Text.Encoding.UTF8.GetBytes(state)))
            {
                return Results.BadRequest();
            }

            var intent = parts.Length > 1 ? parts[1] : SignInIntent;
            var returnTo = parts.Length > 2 ? parts[2] : "/";

            using var client = clients.CreateClient("oauth");
            var redirect = CallbackUrl(siteOptions, http, provider);

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, options.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = options.ClientId,
                    ["client_secret"] = options.ClientSecret,
                    ["code"] = code,
                    ["redirect_uri"] = redirect,
                    ["grant_type"] = "authorization_code",
                }),
            };
            tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var tokenResponse = await client.SendAsync(tokenRequest, ct);
            if (!tokenResponse.IsSuccessStatusCode) return Results.Problem("Token exchange failed.");

            using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
            if (!tokenJson.RootElement.TryGetProperty("access_token", out var accessToken))
            {
                return Results.Problem("The provider returned no access token.");
            }

            using var userRequest = new HttpRequestMessage(HttpMethod.Get, options.UserEndpoint);
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.GetString());
            userRequest.Headers.UserAgent.ParseAdd("ksamods.gg");

            using var userResponse = await client.SendAsync(userRequest, ct);
            if (!userResponse.IsSuccessStatusCode) return Results.Problem("Could not read the provider profile.");

            using var user = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(ct));
            var profile = ReadProfile(provider, user.RootElement);
            if (profile is null) return Results.Problem("The provider profile was not understood.");

            // ── linking an extra provider to the account already signed in ──
            if (intent == LinkIntent)
            {
                // Re-checked here, not just at start: the session can expire while the user is
                // away at the provider, and without a session there is nothing to attach to.
                if (http.User() is not { } signedIn)
                {
                    return LinkFailed(returnTo, "session_expired");
                }

                var link = await accounts.LinkIdentityAsync(
                    signedIn.AccountId, provider, profile.Value.Subject, profile.Value.Login, ct);

                // No new session: they were already signed in, and issuing one here would quietly
                // reset the expiry of a session the user did not touch.
                return Results.LocalRedirect(SafeReturn(returnTo) + link switch
                {
                    LinkResult.Linked => "?linked=" + Uri.EscapeDataString(provider),
                    LinkResult.AlreadyYours => "?linked=" + Uri.EscapeDataString(provider),
                    _ => "?link_error=taken",
                });
            }

            var accountId = await accounts.UpsertAsync(
                provider, profile.Value.Subject, profile.Value.DisplayName,
                profile.Value.Login, profile.Value.AvatarUrl, ct);

            var (sessionId, expires) = await sessions.IssueAsync(
                accountId,
                http.Request.Headers.UserAgent.ToString(),
                http.Connection.RemoteIpAddress?.ToString(),
                ct);

            http.Response.Cookies.Append(sessionOptions.CookieName, sessionId.ToString(), new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = expires,
                Path = "/",
            });

            // Only ever redirect to a local path: an open redirect here would let a phishing link
            // launder itself through the sign-in flow.
            return Results.LocalRedirect(returnTo.StartsWith('/') ? returnTo : "/");
        });

        app.MapPost("/auth/logout", async (
            HttpContext http, SessionStore sessions, string? returnTo, CancellationToken ct) =>
        {
            if (http.Request.Cookies.TryGetValue(sessionOptions.CookieName, out var raw) &&
                Guid.TryParse(raw, out var sessionId))
            {
                // Revoked server-side, not just cleared client-side. Deleting the cookie alone
                // would leave a session anyone holding the old value could still use.
                await sessions.RevokeAsync(sessionId, ct);
            }

            http.Response.Cookies.Delete(sessionOptions.CookieName);

            // A browser posting a form needs somewhere to land; an API client wants the 204.
            // Local paths only - an absolute returnTo here would be an open redirect.
            return returnTo is { Length: > 0 }
                && returnTo.StartsWith('/')
                && !returnTo.StartsWith("//", StringComparison.Ordinal)
                    ? Results.Redirect(returnTo)
                    : Results.NoContent();
        });
    }

    /// <summary>Sends a failed link back to where it started, with something the page can explain.</summary>
    private static IResult LinkFailed(string returnTo, string reason) =>
        Results.LocalRedirect($"{SafeReturn(returnTo)}?link_error={Uri.EscapeDataString(reason)}");

    /// <summary>
    /// Local paths only. An absolute returnTo would make the sign-in flow an open redirect - a
    /// phishing link laundering itself through a domain the user already trusts.
    /// </summary>
    private static string SafeReturn(string? returnTo) =>
        returnTo is { Length: > 0 }
        && returnTo.StartsWith('/')
        && !returnTo.StartsWith("//", StringComparison.Ordinal)
        && !returnTo.Contains('?', StringComparison.Ordinal)
            ? returnTo
            : "/";

    /// <summary>
    /// The provider callback URL, identical in both the authorize redirect and the token
    /// exchange - providers compare the two and reject a mismatch.
    ///
    /// <para>Prefers configuration. Falls back to the request only for local development, where
    /// there is no proxy in front and the host is genuinely what the browser used.</para>
    /// </summary>
    private static string CallbackUrl(SiteOptions site, HttpContext http, string provider)
    {
        var baseUrl = site.Normalised ?? $"{http.Request.Scheme}://{http.Request.Host}";
        return $"{baseUrl}/auth/{provider}/callback";
    }

    /// <summary>
    /// Names the exact setting when the site's public URL is missing behind a proxy.
    ///
    /// <para>Without it the flow does not fail - it succeeds into a redirect_uri pointing at an
    /// internal container name, which the user only discovers when the provider shows them a
    /// mismatch error naming a hostname they have never heard of.</para>
    /// </summary>
    private static IResult? PublicUrlMissing(SiteOptions site, HttpContext http)
    {
        if (site.Normalised is not null) return null;

        // A host with no dot and no "localhost" is a container or service name, never something a
        // browser reached directly.
        var host = http.Request.Host.Host;
        var looksInternal = !host.Contains('.', StringComparison.Ordinal)
            && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

        if (!looksInternal) return null;

        return Results.Problem(
            title: "The site's public URL is not configured",
            detail: $"This request arrived with Host '{http.Request.Host}', which is an internal address. "
                  + "Sign-in would send users to a redirect_uri pointing at that name. Set "
                  + "Site__PublicBaseUrl on the API to the address people use, for example "
                  + "https://ksamods.gg, then redeploy.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// A bare 404 here sends whoever deployed this hunting through routing for an endpoint that
    /// exists and is simply unconfigured. Name the environment variables instead.
    /// </summary>
    private static IResult NotConfigured(string provider) => Results.Problem(
        title: "Sign-in is not configured",
        detail: $"No OAuth credentials are set for '{provider}'. Set OAuth__{Capitalise(provider)}__ClientId "
              + $"and OAuth__{Capitalise(provider)}__ClientSecret on the API, then redeploy. "
              + "Until then the site works read-only.",
        statusCode: StatusCodes.Status404NotFound);

    private static string Capitalise(string value) => value.ToLowerInvariant() switch
    {
        "github" => "GitHub",
        "discord" => "Discord",
        _ => value,
    };

    private static (string Subject, string DisplayName, string? Login, string? AvatarUrl)? ReadProfile(
        string provider, JsonElement root) => provider switch
        {
            "github" when root.TryGetProperty("id", out var id) => (
                id.ToString(),
                root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    ? name.GetString()!
                    : root.GetProperty("login").GetString()!,
                root.GetProperty("login").GetString(),
                root.TryGetProperty("avatar_url", out var avatar) ? avatar.GetString() : null),

            "discord" when root.TryGetProperty("id", out var id) => (
                id.GetString()!,
                root.TryGetProperty("global_name", out var global) && global.ValueKind == JsonValueKind.String
                    ? global.GetString()!
                    : root.GetProperty("username").GetString()!,
                root.GetProperty("username").GetString(),
                null),

            _ => null,
        };
}
