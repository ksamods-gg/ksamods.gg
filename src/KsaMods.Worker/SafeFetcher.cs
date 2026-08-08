using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace KsaMods.Worker;

public sealed record FetchPolicy
{
    /// <summary>
    /// Registered asset hosts of the supported forges (backend.md §5.6). Self-hosted instances
    /// are added individually and never by pattern: a wildcard over "any GitLab" is an open
    /// redirect into arbitrary hosts wearing a forge's clothes.
    /// </summary>
    public IReadOnlySet<string> AllowedHosts { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "codeload.github.com",
    };

    public int MaxRedirects { get; init; } = 5;
    public long MaxBytes { get; init; } = 50L * 1024 * 1024;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);
}

public sealed record FetchResult
{
    public required string FinalUrl { get; init; }
    public required byte[] Sha256 { get; init; }
    public required long Size { get; init; }
    public required string ContentType { get; init; }
    public string? ETag { get; init; }
    public required string TempFilePath { get; init; }
}

public sealed class FetchRejectedException(string reason) : Exception(reason);

/// <summary>
/// Stage 1 and 2, on the host rather than in the container (backend.md §7.1).
///
/// <para>The split is the point: SSRF is a network-policy problem and is solved here, where DNS
/// and egress can actually be controlled; malicious archive content is a parsing problem and is
/// solved in a container that has no network at all. Fetching inside the container would force
/// it to have egress, which is precisely what devalues the rest of the isolation.</para>
/// </summary>
public sealed class SafeFetcher(FetchPolicy policy, string scratchDirectory)
{
    public async Task<FetchResult> FetchAsync(Uri url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(policy.Timeout);

        var current = url;
        for (var redirect = 0; redirect <= policy.MaxRedirects; redirect++)
        {
            // Re-checked on EVERY hop. Validating only the initial URL is the standard way this
            // is got wrong: a permitted host can redirect anywhere.
            var address = await ResolveAndAuthoriseAsync(current, cts.Token);

            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(10),

                // Pin the connection to the address we just authorised, so DNS cannot change
                // between the check and the connect.
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            };

            using var client = new HttpClient(handler) { Timeout = policy.Timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ksamods.gg-validator/1.0");

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location
                    ?? throw new FetchRejectedException("Redirect response carried no Location header.");

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new FetchRejectedException($"The host returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            if (response.Content.Headers.ContentLength is { } declared && declared > policy.MaxBytes)
            {
                throw new FetchRejectedException(
                    $"The asset declares {declared} bytes; the limit is {policy.MaxBytes}.");
            }

            return await StreamToDiskAsync(response, current, cts.Token);
        }

        throw new FetchRejectedException($"Exceeded {policy.MaxRedirects} redirects.");
    }

    private async Task<FetchResult> StreamToDiskAsync(
        HttpResponseMessage response, Uri finalUrl, CancellationToken ct)
    {
        Directory.CreateDirectory(scratchDirectory);
        var path = Path.Combine(scratchDirectory, $"{Guid.NewGuid():N}.zip");

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var destination = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);

        using var sha = SHA256.Create();
        var buffer = new byte[64 * 1024];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;

            total += read;
            // Enforced against the bytes actually delivered, not the declared length: a lying
            // Content-Length must not be able to exhaust the disk.
            if (total > policy.MaxBytes)
            {
                destination.Close();
                File.Delete(path);
                throw new FetchRejectedException($"The asset exceeded {policy.MaxBytes} bytes while downloading.");
            }

            sha.TransformBlock(buffer, 0, read, null, 0);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        sha.TransformFinalBlock([], 0, 0);

        return new FetchResult
        {
            FinalUrl = finalUrl.ToString(),
            Sha256 = sha.Hash!,
            Size = total,
            ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            ETag = response.Headers.ETag?.Tag,
            TempFilePath = path,
        };
    }

    private async Task<IPAddress> ResolveAndAuthoriseAsync(Uri url, CancellationToken ct)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            throw new FetchRejectedException($"Only https is permitted; got '{url.Scheme}'.");
        }

        if (!policy.AllowedHosts.Contains(url.Host))
        {
            throw new FetchRejectedException(
                $"'{url.Host}' is not a registered asset host of a supported forge.");
        }

        var addresses = await Dns.GetHostAddressesAsync(url.Host, ct);
        foreach (var address in addresses)
        {
            if (!IsPubliclyRoutable(address)) continue;
            return address;
        }

        throw new FetchRejectedException(
            $"'{url.Host}' resolved to no publicly routable address.");
    }

    /// <summary>
    /// Rejects loopback, link-local, private, CGNAT, multicast and reserved ranges, in both
    /// address families and including IPv4-mapped IPv6 forms.
    /// </summary>
    internal static bool IsPubliclyRoutable(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();

            if (b[0] == 0) return false;                                  // 0.0.0.0/8
            if (b[0] == 10) return false;                                 // private
            if (b[0] == 127) return false;                                // loopback
            if (b[0] == 169 && b[1] == 254) return false;                 // link-local
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;    // private
            if (b[0] == 192 && b[1] == 168) return false;                 // private
            if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;      // IETF protocol
            if (b[0] == 192 && b[1] == 0 && b[2] == 2) return false;      // TEST-NET-1
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;   // CGNAT
            if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return false;  // benchmarking
            if (b[0] >= 224) return false;                                // multicast + reserved
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
            if (address.Equals(IPAddress.IPv6Any)) return false;

            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                      // unique local fc00::/7
            return true;
        }

        return false;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
