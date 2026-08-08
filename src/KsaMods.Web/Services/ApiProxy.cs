using System.Linq;
using System.Net;

namespace KsaMods.Web.Services;

/// <summary>
/// Reverse-proxies <c>/api</c> and <c>/auth</c> to the backend API so the browser sees a single
/// origin (backend.md §4.1, and the frontend's same-origin decision).
///
/// <para>This is deliberately a dumb pipe. It forwards the method, path, query, body and the
/// headers that matter, and copies the response back including <c>Set-Cookie</c>. It adds no
/// authentication of its own: the API's session cookie is the only credential, and it travels
/// end to end untouched.</para>
/// </summary>
public static class ApiProxy
{
    public const string ClientName = "ksamods-api";

    /// <summary>
    /// Headers the proxy must not copy. Connection-level headers are per-hop by definition, and
    /// forwarding a stale Content-Length or a chunked Transfer-Encoding onto a re-framed body is
    /// how a proxy starts truncating responses.
    /// </summary>
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host", "Content-Length",
    };

    public static void MapApiProxy(this WebApplication app)
    {
        app.Map("/api/{**path}", Forward);
        app.Map("/auth/{**path}", Forward);
    }

    private static async Task Forward(HttpContext context, IHttpClientFactory factory)
    {
        var client = factory.CreateClient(ClientName);

        var target = new UriBuilder(client.BaseAddress!)
        {
            Path = context.Request.Path,
            Query = context.Request.QueryString.Value ?? string.Empty,
        }.Uri;

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);

        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            request.Content = new StreamContent(context.Request.Body);

            if (context.Request.ContentType is { } contentType)
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
        }

        foreach (var header in context.Request.Headers)
        {
            if (HopByHop.Contains(header.Key)) continue;
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)) continue;

            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        // The API rate-limits and hashes by client address, and sets Secure cookies based on the
        // scheme it thinks it is serving. Without these it would see the proxy, not the caller.
        request.Headers.TryAddWithoutValidation("X-Forwarded-For",
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);

        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        context.Response.StatusCode = (int)response.StatusCode;

        foreach (var header in response.Headers)
        {
            if (HopByHop.Contains(header.Key)) continue;
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in response.Content.Headers)
        {
            if (HopByHop.Contains(header.Key)) continue;
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        // Let Kestrel frame the response itself; the upstream length no longer applies once the
        // body has been re-read.
        context.Response.Headers.Remove("transfer-encoding");

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
}

/// <summary>Thrown when the API answers with something the UI should surface rather than swallow.</summary>
public sealed class ApiException(HttpStatusCode status, string? detail)
    : Exception(detail ?? $"The API returned {(int)status}.")
{
    public HttpStatusCode Status { get; } = status;
    public bool IsNotFound => Status == HttpStatusCode.NotFound;
    public bool IsUnauthorised => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
