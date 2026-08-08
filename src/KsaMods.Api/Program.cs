using KsaMods.Api;
using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Api.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;

// Container healthcheck. The runtime image is chiselled - no shell, no curl, no wget - so the
// only thing available to probe the app is the app itself. Docker runs `KsaMods.Api --healthcheck`
// and reads the exit code.
if (args.Contains("--healthcheck"))
{
    return await HealthProbe.RunAsync(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080");
}

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Database=ksamods;Username=ksamods;Password=ksamods";

builder.Services.AddSingleton(Database.CreateDataSource(connectionString));
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddScoped<ModRepository>();
builder.Services.AddScoped<ModlistRepository>();
builder.Services.AddScoped<AccountStore>();
builder.Services.AddScoped<SessionStore>();

var sessionOptions = new SiteSessionOptions();
builder.Services.AddSingleton(sessionOptions);

// The address people actually type. Used to build the OAuth redirect_uri, which must match what
// is registered with the provider exactly - see OAuthEndpoints.SiteOptions for why this is
// configured rather than read off the request.
var siteOptions = new OAuthEndpoints.SiteOptions
{
    PublicBaseUrl = builder.Configuration["Site:PublicBaseUrl"],
};
builder.Services.AddSingleton(siteOptions);

builder.Services.AddHttpClient("oauth");
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression();

// Reads are unauthenticated and heavily cached at the CDN; writes are per-account. Both limits
// exist because this is a public API with no key (§10.3, §14.8).
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    limiter.AddPolicy("reads", http =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
            }));

    limiter.AddPolicy("writes", http =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            http.User.Identity?.Name ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
            }));
});

var app = builder.Build();

// The API sits behind the frontend's proxy and a CDN, so the client address and scheme come from
// forwarded headers. Without this the SSRF-adjacent bits - rate limiting by IP, the ip_hash on a
// session - all record the proxy instead of the caller.
//
// KnownNetworks and KnownProxies must be cleared or the headers are silently ignored: the
// defaults trust only loopback, and in a container the caller is always another address. Safe
// because this service is never exposed directly - see the compose file, which gives it no
// published port.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost,
    KnownIPNetworks = { },
    KnownProxies = { },
});

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseResponseCompression();
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // The API serves JSON only; a CSP this strict costs nothing and closes the case where a
    // response is ever rendered directly by a browser.
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    await next();
});

app.UseCurrentUser();

var providers = new Dictionary<string, OAuthProviderOptions>(StringComparer.OrdinalIgnoreCase);

if (builder.Configuration["OAuth:GitHub:ClientId"] is { Length: > 0 } githubId)
{
    providers["github"] = new OAuthProviderOptions
    {
        ClientId = githubId,
        ClientSecret = builder.Configuration["OAuth:GitHub:ClientSecret"] ?? "",
        AuthorizeEndpoint = "https://github.com/login/oauth/authorize",
        TokenEndpoint = "https://github.com/login/oauth/access_token",
        UserEndpoint = "https://api.github.com/user",
        Scope = "read:user",
    };
}

if (builder.Configuration["OAuth:Discord:ClientId"] is { Length: > 0 } discordId)
{
    providers["discord"] = new OAuthProviderOptions
    {
        ClientId = discordId,
        ClientSecret = builder.Configuration["OAuth:Discord:ClientSecret"] ?? "",
        AuthorizeEndpoint = "https://discord.com/oauth2/authorize",
        TokenEndpoint = "https://discord.com/api/oauth2/token",
        UserEndpoint = "https://discord.com/api/users/@me",
        Scope = "identify",
    };
}

app.MapOAuth(providers, sessionOptions, siteOptions);

if (providers.Count > 0 && siteOptions.Normalised is null)
{
    app.Logger.LogWarning(
        "Sign-in is configured but Site:PublicBaseUrl is not. The OAuth redirect_uri will be "
        + "derived from the request host, which behind a proxy is an internal container name. "
        + "Set Site__PublicBaseUrl to the address people use.");
}
else if (siteOptions.Normalised is { } publicUrl)
{
    app.Logger.LogInformation(
        "Public base URL is {PublicBaseUrl}; OAuth callbacks will use {Callback}.",
        publicUrl, $"{publicUrl}/auth/<provider>/callback");
}
app.MapReadEndpoints();
app.MapModEndpoints();
app.MapModlistEndpoints();
app.MapAccountEndpoints();
app.MapAdminEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

return 0;

public partial class Program;
