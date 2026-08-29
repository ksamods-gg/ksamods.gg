using System.Text.Json;
using Dapper;
using KsaMods.Api.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KsaMods.Worker;

/// <summary>What a handler is: a job kind, and something that does it.</summary>
public interface IJobHandler
{
    string Kind { get; }

    Task HandleAsync(JsonElement payload, CancellationToken ct);
}

/// <summary>
/// Claims jobs and runs them, one at a time, until told to stop (backend.md §11).
///
/// <para><b>Serial on purpose.</b> A job here downloads somebody's archive and runs a container
/// over it; two at once on a small host means two containers, two copies of a 50 MB file and a
/// memory limit that stops meaning anything. Scaling is another worker process, which
/// <c>FOR UPDATE SKIP LOCKED</c> already supports - and that scales across hosts rather than
/// inside one.</para>
///
/// <para>Every handler must be idempotent. At-least-once is the only delivery guarantee worth
/// building on: the process can die between finishing the work and marking the job done, and the
/// next worker will pick it up again.</para>
/// </summary>
public sealed class JobPump(
    JobQueue queue,
    Database database,
    IEnumerable<IJobHandler> handlers,
    IHostApplicationLifetime lifetime,
    ILogger<JobPump> log) : BackgroundService
{
    /// <summary>How long to wait when there was nothing to do. Long enough not to spin.</summary>
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A job whose worker died holds <c>running</c> forever. Anything locked for longer than this
    /// is assumed abandoned and returned to the queue.
    /// </summary>
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(30);

    /// <summary>Names this process in <c>job.locked_by</c>, so a stuck job says who had it.</summary>
    private readonly string _workerId = $"{Environment.MachineName}-{Environment.ProcessId}";

    private readonly Dictionary<string, IJobHandler> _handlers =
        handlers.ToDictionary(h => h.Kind, StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        log.LogInformation(
            "Worker {WorkerId} started. Handling: {Kinds}",
            _workerId, string.Join(", ", _handlers.Keys));

        var sinceSweep = DateTimeOffset.UtcNow;

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - sinceSweep > StuckAfter)
                {
                    await ReleaseStuckAsync(stopping);
                    sinceSweep = DateTimeOffset.UtcNow;
                }

                if (!await RunOneAsync(stopping))
                {
                    await Task.Delay(Idle, stopping);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                // The loop itself failing is different from a job failing: the database may be
                // down, and hammering it makes that worse.
                log.LogError(e, "The job loop threw. Backing off.");
                await Delay(Idle, stopping);
            }
        }

        log.LogInformation("Worker {WorkerId} stopping.", _workerId);
    }

    private async Task<bool> RunOneAsync(CancellationToken ct)
    {
        var job = await queue.ClaimAsync(_workerId, ct);
        if (job is null) return false;

        if (!_handlers.TryGetValue(job.Kind, out var handler))
        {
            // Not a failure to retry: no amount of trying will teach this process a kind it was
            // not built with. Dead, loudly, so the admin queue shows it.
            log.LogError("No handler for job kind '{Kind}' (job {JobId}).", job.Kind, job.Id);
            await queue.FailAsync(job.Id, int.MaxValue, $"No handler for '{job.Kind}'.", ct);
            return true;
        }

        var started = DateTimeOffset.UtcNow;
        log.LogInformation("Job {JobId} {Kind} starting (attempt {Attempt}).", job.Id, job.Kind, job.Attempts);

        try
        {
            using var document = JsonDocument.Parse(job.Payload);
            await handler.HandleAsync(document.RootElement, ct);

            await queue.CompleteAsync(job.Id, ct);
            log.LogInformation(
                "Job {JobId} done in {Seconds:0.0}s.", job.Id, (DateTimeOffset.UtcNow - started).TotalSeconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown, not failure. Leave it locked: the sweep returns it, and marking it failed
            // would spend one of its attempts on a deployment.
            log.LogInformation("Job {JobId} interrupted by shutdown.", job.Id);
            throw;
        }
        catch (ValidatorUnavailableException e)
        {
            // We cannot validate anything, and this release did nothing wrong. Failing the job
            // would spend one of its five attempts on our own broken plumbing and write our
            // infrastructure fault into last_error, where its author reads it - and the next job
            // would do the same, so a missing image quietly empties the queue while the worker
            // sits there looking healthy.
            //
            // Startup already refuses to run without a validator image. This is the same rule held
            // for the rest of the process's life: put the job back untouched and stop, so the
            // restart re-resolves the tag the one way that is legitimate, at startup.
            log.LogCritical(e, "Job {JobId} could not run and no release is at fault. Stopping.", job.Id);

            await queue.ReleaseAsync(job.Id, CancellationToken.None);
            lifetime.StopApplication();

            // Shutdown is asynchronous, so returning "there was work" would have the loop claim
            // another job and fail it the same way before the stop lands. False sends it through
            // the idle delay instead, which shutdown comfortably wins.
            return false;
        }
        catch (Exception e)
        {
            log.LogError(e, "Job {JobId} {Kind} failed.", job.Id, job.Kind);
            await queue.FailAsync(job.Id, job.Attempts, Describe(e), ct);
        }

        return true;
    }

    /// <summary>
    /// Returns jobs whose worker never came back. Without this a deploy in the middle of an import
    /// loses that job permanently, and the only sign is a listing that never gains a release.
    /// </summary>
    private async Task ReleaseStuckAsync(CancellationToken ct)
    {
        using var connection = await database.OpenAsync(ct);

        var released = await connection.ExecuteAsync("""
            update job set state = 'queued', locked_by = null, locked_at = null
            where state = 'running' and locked_at < now() - @stuckAfter
            """,
            new { stuckAfter = StuckAfter });

        if (released > 0) log.LogWarning("Returned {Count} abandoned job(s) to the queue.", released);
    }

    /// <summary>
    /// What goes in <c>last_error</c>, which the author sees on their own import. The message
    /// without the stack: "the release has no .zip asset" is actionable, a stack trace is not.
    /// </summary>
    private static string Describe(Exception e) =>
        e is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
            ? Describe(aggregate.InnerExceptions[0])
            : $"{e.GetType().Name}: {e.Message}";

    private static async Task Delay(TimeSpan duration, CancellationToken ct)
    {
        try
        {
            await Task.Delay(duration, ct);
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-backoff is not worth an exception escaping the loop.
        }
    }
}
