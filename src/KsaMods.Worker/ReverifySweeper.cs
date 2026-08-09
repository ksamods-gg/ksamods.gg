using Dapper;
using KsaMods.Api.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KsaMods.Worker;

/// <summary>How often a published release is re-checked, and how many go per sweep.</summary>
public sealed record ReverifyPolicy
{
    /// <summary>
    /// How stale a check may get before it is worth repeating. Long enough that the site is not
    /// hammering somebody's forge over releases nobody touched; short enough that a dead link or
    /// a swapped archive is caught in days rather than never.
    /// </summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How many to enqueue per sweep. A cap rather than "everything due", because the first sweep
    /// after this ships has the entire catalogue due at once and queueing all of it would spend
    /// the day downloading instead of importing.
    /// </summary>
    public int BatchSize { get; init; } = 25;

    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Finds releases whose verification has gone stale and queues them for re-checking.
///
/// <para>Split from the handler so the actual work goes through the job queue like everything
/// else: retries, backoff and the dead letter state are already solved there, and a sweeper that
/// downloaded files itself would be a second, worse copy of the pump.</para>
/// </summary>
public sealed class ReverifySweeper(
    Database database,
    JobQueue jobs,
    ReverifyPolicy policy,
    ILogger<ReverifySweeper> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation(
            "Re-verification sweeping every {Interval}, re-checking anything older than {MaxAge}.",
            policy.SweepInterval, policy.MaxAge);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // A sweep that throws must not take the sweeper down with it, or one bad night
                // silently ends re-verification for as long as the process lives.
                log.LogError(e, "Re-verification sweep failed. Trying again next interval.");
            }

            try
            {
                await Task.Delay(policy.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        // Never-checked first, then oldest. A release that has never been verified is the one
        // making a claim nothing has ever stood behind.
        //
        // The not-exists clause is what stops a sweep queueing work the last sweep already
        // queued: a job takes minutes and the interval is longer than that, but a slow forge or a
        // retry can straddle two sweeps, and duplicate downloads of the same archive are pure
        // waste on somebody else's bandwidth.
        var due = await connection.QueryAsync<long>("""
            select r.id
            from mod_release r
            join mod m on m.id = r.mod_id
            where r.yanked_at is null
              and m.listing_state in ('listed', 'unlisted')
              and r.validation_state in ('passed', 'passed_warnings')
              and (r.last_verified_at is null or r.last_verified_at < now() - @maxAge)
              and not exists (
                    select 1 from job j
                    where j.kind = 'reverify_release'
                      and j.state in ('queued', 'running')
                      and (j.payload ->> 'releaseId')::bigint = r.id)
            order by r.last_verified_at nulls first
            limit @batch
            """,
            new { maxAge = policy.MaxAge, batch = policy.BatchSize });

        var ids = due.ToList();
        if (ids.Count == 0) return;

        foreach (var id in ids)
        {
            await jobs.EnqueueAsync("reverify_release", new { releaseId = id }, ct);
        }

        log.LogInformation("Queued {Count} release(s) for re-verification.", ids.Count);
    }
}
