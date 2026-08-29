using KsaMods.Exporter;
using Npgsql;

// The exporter (backend.md §12).
//
// Reads the database, serialises it as RFC 0031 documents, and commits the result to a public git
// repository. Runs on a timer, or once and exits with --once.
//
// This is the durability story for a database that is now the record. The mirror is only worth
// anything if forks of it exist before the outage, which is why it is Phase 1 work and not a
// later nicety: a backup nobody has a copy of is a backup that has not been tested.

var once = args.Contains("--once");
var dryRun = args.Contains("--dry-run");

var connectionString =
    Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=ksamods;Username=ksamods;Password=ksamods";

var workingDirectory = Environment.GetEnvironmentVariable("MIRROR_DIRECTORY") ?? "/mirror";
var branch = Environment.GetEnvironmentVariable("MIRROR_BRANCH") ?? "main";

// Where the mirror lives. Without it the exporter still commits - to a local repository inside
// its own volume, which is a real history that nobody can clone. That is the sensible default for
// a deployment that has not decided where the mirror belongs yet, and useless as a durability
// story: §12.1 only works if forks exist before the outage.
//
// Usually carries a token: https://x-access-token:TOKEN@github.com/org/ksamods-index.git
var remoteUrl = Environment.GetEnvironmentVariable("MIRROR_REPO_URL");

// Push is opt-in even with a remote set. An exporter that pushes by default is one that pushes
// from somebody's laptop during a debugging session, and the mirror's history is public.
var push = Environment.GetEnvironmentVariable("MIRROR_PUSH") is "true" or "1";

var interval = TimeSpan.FromMinutes(
    int.TryParse(Environment.GetEnvironmentVariable("EXPORT_INTERVAL_MINUTES"), out var minutes)
        ? Math.Clamp(minutes, 1, 24 * 60)
        : 15);

await using var source = ExporterDatabase.CreateDataSource(connectionString);

using var stopping = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    // Mid-commit is the one moment worth finishing: SIGINT during `git commit` leaves a working
    // tree that the next run has to reason about.
    e.Cancel = true;
    stopping.Cancel();
};

var exitCode = 0;

Log($"export: mirror {workingDirectory}, remote {MirrorBootstrap.Redact(remoteUrl)}, "
  + $"branch {branch}, push {(push ? "on" : "off")}");

if (push && string.IsNullOrWhiteSpace(remoteUrl))
{
    // Pushing to a remote nobody configured cannot work, and failing here says so once rather
    // than every fifteen minutes from inside git.
    Log("export: FATAL - MIRROR_PUSH is on but MIRROR_REPO_URL is not set.");
    return 2;
}

do
{
    try
    {
        // Every run, not just the first: the volume can be recreated under a running service, and
        // a remote that changed should take effect without anybody entering the container.
        await MirrorBootstrap.PrepareAsync(workingDirectory, remoteUrl, branch, stopping.Token);

        var changed = await RunAsync(source, workingDirectory, branch, push, dryRun, stopping.Token);
        Log(changed ? "export: committed" : "export: nothing changed");
    }
    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
    {
        break;
    }
    catch (Exception e)
    {
        // A failed run is not fatal to the schedule: the database may be briefly unreachable, and
        // the next run exports the same state. It is fatal to --once, which is what a deploy
        // pipeline checks.
        Log($"export: FAILED - {e.GetType().Name}: {e.Message}");
        exitCode = 1;

        if (once) break;
    }

    if (once) break;

    try
    {
        await Task.Delay(interval, stopping.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}
while (!stopping.IsCancellationRequested);

return exitCode;

static async Task<bool> RunAsync(
    NpgsqlDataSource source, string workingDirectory, string branch,
    bool push, bool dryRun, CancellationToken ct)
{
    await using var connection = await source.OpenConnectionAsync(ct);

    // No clock anywhere in the build. spec/snapshot.md forbids a wall-clock field outright: it
    // would change the bytes on every scheduled rebuild and invalidate every cached copy for no
    // change in content.
    var input = await ExportReader.ReadAsync(connection, ct);
    var export = IndexBuilder.Build(input);

    Log($"export: {input.Listings.Count} listing(s), {input.Releases.Count} release(s), "
      + $"{input.Modlists.Count} modlist version(s) -> {export.Files.Count} file(s)");

    foreach (var (id, reason) in export.Skipped)
    {
        // Never silent. A listing dropped for not satisfying RFC 0031 is a thing its author can
        // fix, and it can only be fixed by somebody being told.
        Log($"export: skipped {id} - {reason}");
    }

    if (dryRun)
    {
        Log("export: dry run, nothing written");
        return false;
    }

    // Asked before writing, not after. index.json carries the timestamp above, so writing first
    // and letting git decide would produce a commit on every run forever - and a mirror whose
    // history is fifteen-minute noise is one nobody can read a change out of.
    if (!ExportComparer.DiffersFrom(export, workingDirectory))
    {
        return false;
    }

    var mirror = new GitMirror(new GitMirrorOptions
    {
        WorkingDirectory = workingDirectory,
        Branch = branch,
        Push = push,
    });

    // A clock in the commit message is fine - git stamps every commit anyway, and this is the
    // mirror's metadata rather than the snapshot's bytes. Only the artifact has to stay timeless.
    var outcome = await mirror.SyncAsync(
        export, $"Index as of {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC", ct);

    if (outcome.Changed)
    {
        Log($"export: {outcome.FilesWritten} written, {outcome.FilesDeleted} deleted, "
          + $"commit {outcome.CommitSha?[..Math.Min(outcome.CommitSha.Length, 12)]}"
          + (push ? ", pushed" : ", not pushed (MIRROR_PUSH is not set)"));
    }

    return outcome.Changed;
}

static void Log(string message) =>
    Console.WriteLine($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z {message}");

/// <summary>
/// The exporter's own connection factory.
///
/// <para>A copy of the API's rather than a reference to it: this process reads the database and
/// writes files, and referencing a web application to borrow one method would put the whole API in
/// the image. The URL handling has to match, which is what the shared test covers.</para>
/// </summary>
internal static class ExporterDatabase
{
    public static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(ConnectionUrl.Normalise(connectionString));
        return builder.Build();
    }
}
