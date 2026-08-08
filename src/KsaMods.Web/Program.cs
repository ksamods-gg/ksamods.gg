using Microsoft.AspNetCore.HttpOverrides;
using KsaMods.Web;
using KsaMods.Web.Components;
using KsaMods.Web.Services;

// Container healthcheck - the runtime image is chiselled, so the app probes itself. See
// HealthProbe for why that is preferable to adding curl to the image.
if (args.Contains("--healthcheck"))
{
    return await HealthProbe.RunAsync(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080");
}

var builder = WebApplication.CreateBuilder(args);

var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://127.0.0.1:5199";

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();

// Turning on a site notice is a config change and a restart, not a deploy.
builder.Services.Configure<SiteBannerOptions>(builder.Configuration.GetSection("SiteBanner"));

// One HttpClient for both the typed client and the proxy. AllowAutoRedirect is off because the
// OAuth flow's redirects belong to the browser, not to us - we forward them verbatim.
builder.Services.AddHttpClient(ApiProxy.ClientName, client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
});

builder.Services.AddScoped<KsaModsApi>();

// Singleton: the answer comes from the API's configuration, so caching it across requests is the
// point - the layout would otherwise ask on every page render.
builder.Services.AddSingleton<AuthAvailability>();

// Behind a TLS-terminating reverse proxy (Coolify's Traefik, nginx, a CDN), the app only ever
// sees plain HTTP on an internal address. Without this it believes every request is insecure,
// which breaks two things badly:
//
//   · UseHttpsRedirection redirects to https, the proxy forwards the retry as http again, and the
//     browser loops until it gives up.
//   · The OAuth start endpoint builds its redirect_uri from Request.Scheme, so it would send
//     users to http://…/auth/github/callback - which will not match the registered callback.
//
// KnownNetworks and KnownProxies are cleared because the defaults trust only loopback, and in a
// container the proxy is always a different address. That is safe here precisely because the app
// is not reachable except through the proxy; never do it on a directly-exposed host.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

// Only when the app terminates TLS itself. Behind a proxy the redirect is the proxy's job, and
// doing it here is how the loop above starts.
if (builder.Configuration.GetValue("BehindProxy", true) is false)
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();
app.MapStaticAssets();

// Everything the browser touches is one origin: the Blazor server forwards /api and /auth to the
// .NET API. That is what lets the API keep its HttpOnly, SameSite=Lax session cookie with no CORS
// policy and no token handling in JavaScript - the browser only ever sees ksamods.gg.
app.MapApiProxy();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Liveness only: it deliberately does not touch the API, so a container orchestrator restarting
// the frontend because the backend is briefly down cannot turn one outage into two.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

return 0;

public partial class Program;
