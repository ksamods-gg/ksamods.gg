using Microsoft.Extensions.Configuration;
using KsaMods.Api.Data;
using KsaMods.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The process that does the work the API only promises (backend.md §11).
//
// It is a separate service for one reason: it talks to the Docker daemon, which is
// root-equivalent, on behalf of archives strangers produced. The API - the thing exposed to the
// internet - must not be able to do that, and the only way to mean it is for the API not to have
// the socket.

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration["ConnectionStrings:Postgres"]
    ?? builder.Configuration["DATABASE_URL"]
    ?? "Host=localhost;Database=ksamods;Username=ksamods;Password=ksamods";

// Container healthcheck. There is no port to probe - this process serves nothing - so the useful
// question is whether it can still reach the queue it exists to drain. A worker that cannot is
// indistinguishable from no worker at all, and should be restarted rather than left looking fine.
if (args.Contains("--healthcheck"))
{
    try
    {
        await using var probeSource = Database.CreateDataSource(connectionString);
        await using var probe = await probeSource.OpenConnectionAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "select 1";
        await command.ExecuteScalarAsync();

        return 0;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"healthcheck: {e.GetType().Name}: {e.Message}");
        return 1;
    }
}

builder.Services.AddSingleton(Database.CreateDataSource(connectionString));
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<JobQueue>();

builder.Services.AddHttpClient(ForgeFactory.ClientName);
builder.Services.AddSingleton<IForgeFactory, ForgeFactory>();

// Scratch lives on a tmpfs in the container, so an archive never touches a disk that outlives the
// job. The site does not keep the bytes, and the tidiest way to honour that is to make keeping
// them impossible.
var scratch = builder.Configuration["Worker:Scratch"] ?? Path.Combine(Path.GetTempPath(), "ksamods");
Directory.CreateDirectory(scratch);

builder.Services.AddSingleton(new FetchPolicy());
builder.Services.AddSingleton(services => new SafeFetcher(
    services.GetRequiredService<FetchPolicy>(), scratch));

// Which image every validation runs in. A tag is resolved to the immutable id it currently points
// at, so the deployment can build the image under a stable name and this worker still cannot have
// it swapped underneath it mid-life. An explicit digest is honoured as given.
//
// Refuses to start rather than falling back to something convenient: a validator running an
// unknown image is worse than a validator not running.
var image = await ValidatorImage.ResolveAsync(
    builder.Configuration["Validator:ImageDigest"],
    builder.Configuration["Validator:Image"],
    Console.Error.WriteLine,
    CancellationToken.None);

if (image is null)
{
    Console.Error.WriteLine("FATAL: no validator image. Refusing to start.");
    return 2;
}

builder.Services.AddSingleton(new ContainerPolicy
{
    ImageDigest = image,
    SeccompProfilePath = builder.Configuration["Validator:SeccompProfile"],
});

builder.Services.AddSingleton(services => new ContainerRunner(
    services.GetRequiredService<ContainerPolicy>()));

builder.Services.AddSingleton<IJobHandler, ImportHandler>();

// Re-verification: the sweeper decides what is stale, the handler re-checks one release. Both
// configurable, because "how often is often enough" depends on how much traffic the forges will
// tolerate and that is an operational answer rather than a code one.
builder.Services.AddSingleton(new ReverifyPolicy
{
    MaxAge = TimeSpan.FromDays(builder.Configuration.GetValue("Reverify:MaxAgeDays", 7)),
    BatchSize = builder.Configuration.GetValue("Reverify:BatchSize", 25),
    SweepInterval = TimeSpan.FromMinutes(builder.Configuration.GetValue("Reverify:SweepMinutes", 30)),
});
builder.Services.AddSingleton<IJobHandler, ReverifyHandler>();

// Polling for releases nobody told us about. The webhook stays the fast path where it works; this
// is the floor under it.
builder.Services.AddSingleton(new PollPolicy
{
    Interval = TimeSpan.FromMinutes(builder.Configuration.GetValue("Poll:IntervalMinutes", 20)),
    BatchSize = builder.Configuration.GetValue("Poll:BatchSize", 50),
});
// The game build list. Without it a month bound never resolves to a revision, and every release
// carrying one evaluates as Unknown (RFC 0017).
builder.Services.AddSingleton(new BuildPolicy
{
    MasterUrl = builder.Configuration["Builds:MasterUrl"] ?? new BuildPolicy().MasterUrl,
    BackfillUrl = builder.Configuration["Builds:BackfillUrl"] ?? new BuildPolicy().BackfillUrl,
    Interval = TimeSpan.FromMinutes(builder.Configuration.GetValue("Builds:IntervalMinutes", 60)),
    Backfill = builder.Configuration.GetValue("Builds:Backfill", true),
});
builder.Services.AddHostedService<BuildPoller>();

builder.Services.AddHostedService<ReleasePoller>();
builder.Services.AddHostedService<ReverifySweeper>();
builder.Services.AddHostedService<JobPump>();

var host = builder.Build();

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Worker")
    .LogInformation("Scratch {Scratch}.", scratch);

await host.RunAsync();

return 0;
