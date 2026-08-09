using KsaMods.Forge;

namespace KsaMods.Api.Data;

/// <summary>
/// Hands out the adapter for a provider.
///
/// <para>An interface rather than the concrete client so a test can hand the endpoints a forge
/// that answers from memory. The repository-proof endpoints are the ones most worth testing and
/// the least testable against the real thing: verifying a challenge means committing a file to a
/// repository GitHub knows about.</para>
/// </summary>
public interface IForgeFactory
{
    IForge For(string provider);
}

public sealed class ForgeFactory(IHttpClientFactory clients, IConfiguration configuration) : IForgeFactory
{
    public const string ClientName = "forge";

    public IForge For(string provider) => provider.ToLowerInvariant() switch
    {
        "github" => new GitHubForge(
            Configure(clients.CreateClient(ClientName), "https://api.github.com/"),
            // Optional. Without it GitHub allows 60 requests an hour per address, which is enough
            // for a quiet site and not enough for a busy one; the site reads only public
            // repositories either way, so the token buys rate limit and no access.
            configuration["GitHub:Token"]),

        _ => throw new ForgeException(
            $"No adapter for '{provider}'. Supported: {string.Join(", ", ForgeAllowlist.ApiHosts.Keys)}."),
    };

    private static HttpClient Configure(HttpClient client, string baseAddress)
    {
        client.BaseAddress = new Uri(baseAddress);
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }
}
