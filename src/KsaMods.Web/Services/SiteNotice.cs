using System.Text.Json.Serialization;

namespace KsaMods.Web.Services;

/// <summary>The banner across the top of every page, as the API reports it.</summary>
public sealed record SiteNotice
{
    [JsonPropertyName("message")] public string Message { get; init; } = "";
    [JsonPropertyName("variant")] public string Variant { get; init; } = "info";
    [JsonPropertyName("link_text")] public string? LinkText { get; init; }
    [JsonPropertyName("link_href")] public string? LinkHref { get; init; }
    [JsonPropertyName("dismissible")] public bool Dismissible { get; init; } = true;
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }

    public bool IsActive => !string.IsNullOrWhiteSpace(Message);

    /// <summary>
    /// Identifies this notice to the browser's dismissal memory.
    ///
    /// <para>Derived from the text rather than hand-set, so editing the message publishes a new
    /// notice that everyone sees again. A hand-written id is the kind of thing you forget to bump,
    /// and the failure mode is an incident notice nobody reads.</para>
    /// </summary>
    public string DismissKey
    {
        get
        {
            var material = $"{Message}{LinkHref}";
            var digest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(material));

            return Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant();
        }
    }
}

/// <summary>
/// Caches the notice so the banner does not cost an API call on every page render.
///
/// <para>Short TTL rather than none: this is the thing a moderator reaches for during an outage,
/// and a banner that takes ten minutes to appear is a banner that arrives after the incident. It
/// is also the thing they reach for while the API is unwell, so a failed fetch keeps the last
/// answer instead of blanking a notice that may be the only explanation anybody has.</para>
/// </summary>
public sealed class SiteNoticeCache(IHttpClientFactory factory, ILogger<SiteNoticeCache> log)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _lock = new(1, 1);

    private SiteNotice _notice = new();
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;

    public async Task<SiteNotice> CurrentAsync(CancellationToken ct = default)
    {
        if (DateTimeOffset.UtcNow - _checkedAt < Ttl) return _notice;

        await _lock.WaitAsync(ct);

        try
        {
            if (DateTimeOffset.UtcNow - _checkedAt < Ttl) return _notice;

            using var client = factory.CreateClient(ApiProxy.ClientName);
            var fetched = await client.GetFromJsonAsync<SiteNotice>("/api/v1/notice", ct);

            if (fetched is not null)
            {
                _notice = fetched;
                _checkedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException
                                    or System.Text.Json.JsonException)
        {
            // Keep the last known notice and try again next time. Logged once per TTL at most,
            // so an API outage does not also produce a log flood.
            log.LogDebug(e, "Could not read the site notice; keeping the last one.");
            _checkedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _lock.Release();
        }

        return _notice;
    }

    /// <summary>Drops the cache so an edit shows up on the editor's very next render.</summary>
    public void Invalidate() => _checkedAt = DateTimeOffset.MinValue;
}
