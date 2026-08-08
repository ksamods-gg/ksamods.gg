using KsaMods.Api.Auth;
using KsaMods.Api.Data;
using KsaMods.Api.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;

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

// The API sits behind a CDN and a reverse proxy, so the client address and scheme come from
// forwarded headers. Without this the SSRF-adjacent bits — rate limiting by IP, the ip_hash on a
// session — all record the proxy instead of the caller.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
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

app.MapOAuth(providers, sessionOptions);
app.MapReadEndpoints();
app.MapModEndpoints();
app.MapModlistEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
