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

// DATABASE_URL is what every managed provider gives you, and what the compose file passes. The
// explicit setting wins where both are present, so a deployment can override one service without
// touching the shared value. Either may be a postgres:// URL or libpq key/value - Database
// normalises it.
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["DATABASE_URL"]
    ?? "Host=localhost;Database=ksamods;Username=ksamods;Password=ksamods";

builder.Services.AddSingleton(Database.CreateDataSource(connectionString));
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddScoped<ModRepository>();
builder.Services.AddScoped<ModlistRepository>();
builder.Services.AddScoped<AccountStore>();
builder.Services.AddScoped<TagVocabulary>();
builder.Services.AddScoped<SessionStore>();
builder.Services.AddScoped<TokenStore>();

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
builder.Services.AddHttpClient(ForgeFactory.ClientName);
builder.Services.AddSingleton<IForgeFactory, ForgeFactory>();
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression();

// Reads are unauthenticated and heavily cached at the CDN; writes are per-account. Both limits
// exist because this is a public API with no key (§10.3, §14.8).
// The request budgets, from configuration and changeable at runtime. Held in a singleton the
// partition factories read each time they build one, so lowering a limit during an incident does
// not need a deploy. See RateLimits for what "takes effect" means precisely.
var rateLimits = RateLimits.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(rateLimits);

builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;


    // Anonymous reads stay open, which is the promise. A key lifts the ceiling and moves the
    // bucket off a shared address, so one noisy client cannot spend a whole office's quota and
    // heavy callers become visible instead of anonymous.
    limiter.AddPolicy("reads", http =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            http.RateLimitPartition(),
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = http.HasKey() ? 3000 : 300,
                Window = TimeSpan.FromMinutes(1),
            }));

    // Webhooks have no session, so they cannot share the per-account bucket - every forge in the
    // world would land in one partition and a single busy repository would lock out the rest.
    // Partitioned by the sender instead, and generous: a burst of releases is a normal morning.
    limiter.AddPolicy("webhooks", http => Partition(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown", rateLimits.Webhooks));

    limiter.AddPolicy("writes", http => Partition(
        http.User.Identity?.Name ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        rateLimits.Writes));

    // The key carries the generation, so a changed limit produces different keys and therefore
    // fresh partitions built with the new budget. Reading rateLimits here without that would only
    // affect callers the limiter has never seen. See RateLimits for the measurement.
    System.Threading.RateLimiting.RateLimitPartition<string> Partition(string caller, int permits) =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(rateLimits.Key(caller),
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = rateLimits.Window,
            });
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
app.MapPublicProfileEndpoints();
app.MapAdminEndpoints();
app.MapReportEndpoints();
app.MapNoticeEndpoints();
app.MapBugEndpoints();
app.MapRateLimitEndpoints();
app.MapTagEndpoints();
app.MapTokenEndpoints();

// Off unless a secret is configured: without one, every caller is anonymous and the endpoint is a
// way to make the site do work on request.
app.MapWebhooks(builder.Configuration["GitHub:WebhookSecret"]);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

return 0;

public partial class Program;
