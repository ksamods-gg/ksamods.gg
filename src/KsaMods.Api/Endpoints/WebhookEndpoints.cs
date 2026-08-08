using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using KsaMods.Api.Data;

namespace KsaMods.Api.Endpoints;

/// <summary>
/// Forge webhooks (backend.md §5.4).
///
/// <para>An author who publishes a release should not have to come back here and press a button.
/// The webhook turns "there is a new release" into an import job, and that is all it does - the
/// payload is a notification, never a source of facts. Everything about the release is read back
/// from the forge's API by the worker, because the body of a webhook is whatever was posted to
/// this endpoint.</para>
/// </summary>
public static class WebhookEndpoints
{
    /// <summary>
    /// Bodies are small notifications. A cap stops a hostile sender streaming forever into a
    /// buffer, and there is no legitimate payload anywhere near it.
    /// </summary>
    private const int MaxBody = 256 * 1024;

    public static void MapWebhooks(this IEndpointRouteBuilder app, string? githubSecret)
    {
        app.MapPost("/api/v1/webhooks/github", async (
            HttpContext http, Database database, JobQueue jobs, ILoggerFactory logging,
            CancellationToken ct) =>
        {
            var log = logging.CreateLogger("Webhooks");

            if (string.IsNullOrWhiteSpace(githubSecret))
            {
                // Without a secret every caller is anonymous and the endpoint is a way to make the
                // site do work on request. Off is the only safe unconfigured state.
                return Results.NotFound();
            }

            var body = await ReadAsync(http.Request, ct);
            if (body is null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            if (!SignatureMatches(http.Request, body, githubSecret))
            {
                log.LogWarning("Rejected a GitHub webhook with a bad signature.");
                return Results.Unauthorized();
            }

            var eventName = http.Request.Headers["X-GitHub-Event"].ToString();
            var delivery = http.Request.Headers["X-GitHub-Delivery"].ToString();

            // ping is what GitHub sends when the hook is created. Answering it is how the author
            // finds out the URL and secret are right.
            if (eventName == "ping") return Results.Ok(new { pong = true });

            // A release edited or deleted does not change what has already been imported: a
            // published version is immutable, and unpublishing is the author's `yank`, not ours.
            if (eventName != "release") return Results.Accepted();

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var action = root.TryGetProperty("action", out var a) ? a.GetString() : null;
            if (action is not ("published" or "released")) return Results.Accepted();

            if (!root.TryGetProperty("repository", out var repository)
                || !repository.TryGetProperty("id", out var repoId))
            {
                return Results.Accepted();
            }

            using var connection = await database.OpenAsync(ct);

            // Deliveries repeat: GitHub retries on any non-2xx, and a retry after a successful
            // import must not import again. The primary key does the deduplicating.
            if (!string.IsNullOrWhiteSpace(delivery))
            {
                var first = await connection.ExecuteAsync("""
                    insert into webhook_delivery (id, provider, event)
                    values (@delivery, 'github', @eventName)
                    on conflict (id) do nothing
                    """,
                    new { delivery, eventName });

                if (first == 0) return Results.Ok(new { duplicate = true });
            }

            // Only a verified link. An unproven claim must not be able to trigger work, or the
            // proof is decoration.
            var modId = await connection.ExecuteScalarAsync<string?>("""
                select mod_id from repo_link
                where provider = 'github' and repo_id = @repoId and verified_at is not null
                """,
                new { repoId = repoId.GetRawText() });

            if (modId is null)
            {
                log.LogInformation("GitHub webhook for an unconnected repository {RepoId}.", repoId.GetRawText());
                return Results.Accepted();
            }

            var jobId = await jobs.EnqueueAsync("import_release", new { modId }, ct);

            await connection.ExecuteAsync(
                "update repo_link set last_seen_at = now() where mod_id = @modId", new { modId });

            log.LogInformation("Queued import {JobId} for {ModId} from a webhook.", jobId, modId);

            return Results.Accepted(value: new { job_id = jobId });
        })
        // Deliberately outside the "writes" policy, which partitions by account: a webhook has no
        // session, so every forge in the world would share one bucket and one busy repository
        // could lock out the rest.
        .RequireRateLimiting("webhooks");
    }

    private static async Task<byte[]?> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > MaxBody) return null;

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);

        return buffer.Length > MaxBody ? null : buffer.ToArray();
    }

    /// <summary>
    /// HMAC-SHA256 over the raw body, as GitHub signs it.
    ///
    /// <para>Over the bytes rather than a re-serialised object: any parse-then-reserialise step
    /// changes whitespace and the signature stops matching for reasons that look like a
    /// configuration problem.</para>
    /// </summary>
    private static bool SignatureMatches(HttpRequest request, byte[] body, string secret)
    {
        var header = request.Headers["X-Hub-Signature-256"].ToString();

        if (!header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) return false;

        var expected = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

        // Fixed-time: a byte-at-a-time comparison leaks how much of a guess was right, which is
        // enough to find the rest one request at a time.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(header["sha256=".Length..].ToLowerInvariant()));
    }
}
