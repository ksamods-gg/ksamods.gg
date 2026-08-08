using KsaMods.Web.Components;
using KsaMods.Web.Services;

var builder = WebApplication.CreateBuilder(args);

var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://127.0.0.1:5199";

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();

// One HttpClient for both the typed client and the proxy. AllowAutoRedirect is off because the
// OAuth flow's redirects belong to the browser, not to us — we forward them verbatim.
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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

// Everything the browser touches is one origin: the Blazor server forwards /api and /auth to the
// .NET API. That is what lets the API keep its HttpOnly, SameSite=Lax session cookie with no CORS
// policy and no token handling in JavaScript — the browser only ever sees ksamods.gg.
app.MapApiProxy();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
