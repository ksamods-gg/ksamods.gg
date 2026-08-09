using Dapper;
using KsaMods.Api.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KsaMods.Worker;

/// <summary>How often connected repositories are checked for releases nobody told us about.</summary>
public sealed record PollPolicy
{
    /// <summary>
    /// How long a repository may go unchecked. Every connected repository costs one forge request
    /// per round, so this is the dial between "a new release appears quickly" and "the site is a
    /// well-behaved API client".
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Repositories per round. A cap rather than "all of them", so the first round after a busy
    /// week does not spend an hour queueing imports before anything else runs.
    /// </summary>
    public int BatchSize { get; init; } = 50;
}

/// <summary>
/// Checks connected repositories for releases the site has not seen.
///
/// <para>The webhook is the fast path: GitHub posts within seconds of a release and the import is
/// queued immediately. It is not a reliable path. Nothing here registers a webhook on an author's
/// repository, a delivery can be dropped, and the endpoint is off entirely unless a signing secret
/// is configured. So without polling the honest description of release detection is "an author
/// remembers to press Import", which is not detection at all.</para>
///
/// <para>Polling is the floor, not the ceiling. Where the webhook works, this finds nothing and
/// costs one request; where it does not, this is the only reason a release ever appears.</para>
/// </summary>
public sealed class ReleasePoller(
    Database database,
    JobQueue jobs,
    PollPolicy policy,
    ILogger<ReleasePoller> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation(
            "Polling connected repositories every {Interval}, {Batch} at a time.",
            policy.Interval, policy.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // One bad round must not end polling for the life of the process.
                log.LogError(e, "Release poll failed. Trying again next interval.");
            }

            try
            {
                await Task.Delay(policy.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        // Verified links only. An unproven link is a claim somebody typed, and fetching from it on
        // a timer would let anyone point the site at a repository they do not own and have it
        // politely poll a stranger's server forever.
        //
        // Longest-unpolled first, so every repository gets a turn rather than the same handful
        // being checked while the tail starves.
        var due = await connection.QueryAsync<string>("""
            select l.mod_id
            from repo_link l
            join mod m on m.id = l.mod_id
            where l.verified_at is not null
              and m.listing_state in ('listed', 'unlisted')
              and (l.last_polled_at is null or l.last_polled_at < now() - @interval)
              and not exists (
                    select 1 from job j
                    where j.kind = 'import_release'
                      and j.state in ('queued', 'running')
                      and j.payload ->> 'modId' = l.mod_id)
            order by l.last_polled_at nulls first
            limit @batch
            """,
            new { interval = policy.Interval, batch = policy.BatchSize });

        var ids = due.ToList();
        if (ids.Count == 0) return;

        foreach (var modId in ids)
        {
            // The import handler already asks the forge what releases exist and skips the ones it
            // has. Polling therefore does not need to compare anything itself: it only needs to
            // ask, on a timer, the question the webhook would have asked on an event.
            await jobs.EnqueueAsync("import_release", new { modId }, ct);
        }

        // Stamped whether or not the import finds anything new, because the stamp records that we
        // looked. Marking it only on success would poll a quiet repository on every single round.
        await connection.ExecuteAsync(
            "update repo_link set last_polled_at = now() where mod_id = any(@ids)",
            new { ids = ids.ToArray() });

        log.LogInformation("Polled {Count} repository(ies) for new releases.", ids.Count);
    }
}
