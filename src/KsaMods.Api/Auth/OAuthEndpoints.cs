using System.Net.Http.Headers;
using System.Text.Json;
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
/// the session it produces is our own table rather than a cookie principal — going through the
/// authentication middleware would mean translating between two session models for no gain.</para>
/// </summary>
public static class OAuthEndpoints
{
    private const string StateCookie = "ksamods_oauth_state";

    public static void MapOAuth(
        this IEndpointRouteBuilder app,
        IReadOnlyDictionary<string, OAuthProviderOptions> providers,
        SiteSessionOptions sessionOptions)
    {
        // Which providers actually have credentials. The frontend asks this so it can hide a
        // sign-in button that could only ever 404 — an operator who has not set the credentials
        // gets a working read-only site, not a broken button.
        //
        // Under /api/v1 rather than /auth: this is data about the flow, not a step in it.
        app.MapGet("/api/v1/auth/providers", () => Results.Ok(new
        {
            providers = providers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
        }));

        app.MapGet("/auth/{provider}/start", (string provider, HttpContext http, string? returnTo) =>
        {
            if (!providers.TryGetValue(provider, out var options)) return NotConfigured(provider);

            // CSRF protection for the callback. Bound to the browser via a cookie so a state
            // value alone is not enough to complete somebody else's sign-in.
            var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

            http.Response.Cookies.Append(StateCookie, $"{state}|{returnTo ?? "/"}", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10),
                Path = "/",
            });

            var redirect = $"{http.Request.Scheme}://{http.Request.Host}/auth/{provider}/callback";
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
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)) return Results.BadRequest();

            if (!http.Request.Cookies.TryGetValue(StateCookie, out var stored)) return Results.BadRequest();
            http.Response.Cookies.Delete(StateCookie);

            var parts = stored.Split('|', 2);
            // Constant-time: the state is a secret and a timing oracle on it is a real, if
            // fiddly, CSRF path.
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(parts[0]),
                    System.Text.Encoding.UTF8.GetBytes(state)))
            {
                return Results.BadRequest();
            }

            var returnTo = parts.Length > 1 ? parts[1] : "/";

            using var client = clients.CreateClient("oauth");
            var redirect = $"{http.Request.Scheme}://{http.Request.Host}/auth/{provider}/callback";

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

        app.MapPost("/auth/logout", async (HttpContext http, SessionStore sessions, CancellationToken ct) =>
        {
            if (http.Request.Cookies.TryGetValue(sessionOptions.CookieName, out var raw) &&
                Guid.TryParse(raw, out var sessionId))
            {
                await sessions.RevokeAsync(sessionId, ct);
            }

            http.Response.Cookies.Delete(sessionOptions.CookieName);
            return Results.NoContent();
        });
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
