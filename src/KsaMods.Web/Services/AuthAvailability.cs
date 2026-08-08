using System.Text.Json.Serialization;

namespace KsaMods.Web.Services;

/// <summary>
/// Which sign-in providers the API actually has credentials for.
///
/// <para>The layout renders on every page, so this is cached: the answer comes from the API's
/// configuration and cannot change without a redeploy. A short TTL rather than caching forever so
/// that turning credentials on does not need the frontend restarted too.</para>
///
/// <para>On failure it reports <b>no</b> providers. Offering a sign-in button that leads to a 404
/// is worse than offering none — the reader has no way to tell a misconfiguration from an outage,
/// and either way the button cannot work.</para>
/// </summary>
public sealed class AuthAvailability(IHttpClientFactory factory, ILogger<AuthAvailability> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IReadOnlyList<string> _providers = [];
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<string>> ProvidersAsync(CancellationToken ct = default)
    {
        if (DateTimeOffset.UtcNow - _checkedAt < Ttl) return _providers;

        await _lock.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow - _checkedAt < Ttl) return _providers;

            using var client = factory.CreateClient(ApiProxy.ClientName);
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetFromJsonAsync<ProvidersResponse>("/api/v1/auth/providers", ct);

            _providers = response?.Providers ?? [];
            _checkedAt = DateTimeOffset.UtcNow;

            if (_providers.Count == 0)
            {
                logger.LogInformation(
                    "No OAuth providers are configured on the API; sign-in will not be offered.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not read the API's auth providers; hiding sign-in.");
            _providers = [];
            // Deliberately not stamping _checkedAt: a transient failure should be retried on the
            // next render rather than cached as "no sign-in" for the full TTL.
        }
        finally
        {
            _lock.Release();
        }

        return _providers;
    }

    private sealed record ProvidersResponse
    {
        [JsonPropertyName("providers")]
        public IReadOnlyList<string> Providers { get; init; } = [];
    }
}
