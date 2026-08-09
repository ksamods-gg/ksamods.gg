using System.Text.Json.Serialization;

namespace KsaMods.Web.Services;

/// <summary>How many people are in the Discord, as the counter service reports it.</summary>
public sealed record DiscordPresence
{
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("online")] public int Online { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>
/// Reads the member count off the community's own counter and holds onto it.
///
/// <para>Cached because this is a third party on the render path. Blazor renders the page while
/// <c>OnInitializedAsync</c> is still running, so an uncached fetch would put someone else's
/// latency in front of our own page, once per visitor. One request every few minutes serves
/// everybody, and the timeout is short for the same reason.</para>
///
/// <para>A failed fetch keeps the last good answer rather than blanking the number. A count that
/// is a few minutes stale is fine; a page that reads "members: 0" because a request timed out is
/// not, and neither is one that shuffles between having the figure and not having it.</para>
/// </summary>
public sealed class DiscordPresenceCache(IHttpClientFactory factory, ILogger<DiscordPresenceCache> log)
{
    public const string ClientName = "discord-presence";

    /// <summary>The counter itself only refreshes periodically, so asking faster buys nothing.</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _lock = new(1, 1);

    private DiscordPresence? _presence;
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// The latest counts, or null if we have never managed to read them. Callers render the page
    /// without the figures in that case rather than showing a zero.
    /// </summary>
    public async Task<DiscordPresence?> CurrentAsync(CancellationToken ct = default)
    {
        if (DateTimeOffset.UtcNow - _checkedAt < Ttl) return _presence;

        await _lock.WaitAsync(ct);

        try
        {
            if (DateTimeOffset.UtcNow - _checkedAt < Ttl) return _presence;

            using var client = factory.CreateClient(ClientName);
            var fetched = await client.GetFromJsonAsync<DiscordPresence>("/members", ct);

            // Total of zero means the counter is having a bad day, not that the server emptied.
            // Treating it as data would replace a good number with a wrong one.
            if (fetched is { Total: > 0 }) _presence = fetched;

            _checkedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException
                                    or System.Text.Json.JsonException or UriFormatException)
        {
            // Keep whatever we had. Stamped either way so a service that is down does not mean a
            // fresh outbound request on every single render.
            log.LogDebug(e, "Could not read the Discord member count; keeping the last one.");
            _checkedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _lock.Release();
        }

        return _presence;
    }
}
