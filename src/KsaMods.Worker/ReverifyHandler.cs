using System.Text.Json;
using Dapper;
using KsaMods.Api.Data;
using Microsoft.Extensions.Logging;

namespace KsaMods.Worker;

/// <summary>
/// Re-checks that a published release is still the file we said it was (backend.md §11.1).
///
/// <para>Everything the site claims about a download decays. "Verified 3 days ago" is a statement
/// about a moment, the link belongs to somebody else's forge, and an asset can be deleted or
/// replaced at any time without anybody here being told. Without something that goes back and
/// looks, those claims quietly stop being true and the site keeps making them.</para>
///
/// <para>Three outcomes, and each one is a different sentence to the reader:</para>
/// <list type="bullet">
///   <item><c>verified</c>: the bytes still hash to what was recorded.</item>
///   <item><c>unavailable</c>: the link no longer resolves. The record stays, because modlists
///     may pin this version and a hole in the graph is worse than a row marked gone.</item>
///   <item><c>quarantined</c>: the bytes changed. The download is hidden until a human looks.</item>
/// </list>
///
/// <para>A changed hash quarantines rather than merely flagging. Release assets are not supposed
/// to move: replacing one means somebody deleted and re-uploaded it, which is the shape a
/// compromised release makes. Telling a benign re-pack apart from a real substitution needs the
/// validator's own record of assemblies and asset ids, so until that comparison exists the
/// conservative answer is the safe one. Being wrong here costs an author a message; being wrong
/// the other way costs somebody an unreviewed binary.</para>
/// </summary>
public sealed class ReverifyHandler(
    Database database,
    SafeFetcher fetcher,
    ILogger<ReverifyHandler> log) : IJobHandler
{
    public string Kind => "reverify_release";

    public async Task HandleAsync(JsonElement payload, CancellationToken ct)
    {
        if (!payload.TryGetProperty("releaseId", out var element)
            || !element.TryGetInt64(out var releaseId))
        {
            log.LogWarning("reverify_release job had no releaseId; nothing to do.");
            return;
        }

        using var connection = await database.OpenAsync(ct);

        var artifact = await connection.QuerySingleOrDefaultAsync<ArtifactRow>("""
            select a.url as Url, a.sha256 as Sha256, r.mod_id as ModId, r.version as Version
            from release_artifact a
            join mod_release r on r.id = a.release_id
            where a.release_id = @releaseId and a.is_mirror = false
            limit 1
            """,
            new { releaseId });

        if (artifact is null)
        {
            log.LogWarning("Release {ReleaseId} has no primary artifact to re-check.", releaseId);
            return;
        }

        string availability;
        string outcome;

        try
        {
            var fetched = await fetcher.FetchAsync(new Uri(artifact.Url), ct);

            // Delete the temp file straight away. This job downloads every published release on a
            // schedule, and a worker that leaks one file per run fills its disk in a week.
            TryDelete(fetched.TempFilePath);

            if (fetched.Sha256.AsSpan().SequenceEqual(artifact.Sha256))
            {
                availability = "verified";
                outcome = "unchanged";
            }
            else
            {
                availability = "quarantined";
                outcome = "bytes changed";

                log.LogWarning(
                    "{ModId} {Version} no longer matches its recorded hash. Quarantined pending review.",
                    artifact.ModId, artifact.Version);
            }
        }
        catch (FetchRejectedException e)
        {
            // The host said no, or the file is gone. Either way it is not fetchable now, which is
            // the thing a reader needs to know before they click.
            availability = "unavailable";
            outcome = e.Message;
        }

        await connection.ExecuteAsync("""
            update mod_release
            set availability = @availability, last_verified_at = now()
            where id = @releaseId
            """,
            new { releaseId, availability });

        log.LogInformation(
            "Re-checked {ModId} {Version}: {Availability} ({Outcome}).",
            artifact.ModId, artifact.Version, availability, outcome);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // A temp file we could not remove is not worth failing a job over.
        }
    }

    private sealed record ArtifactRow
    {
        public string Url { get; init; } = "";
        public byte[] Sha256 { get; init; } = [];
        public string ModId { get; init; } = "";
        public string Version { get; init; } = "";
    }
}
