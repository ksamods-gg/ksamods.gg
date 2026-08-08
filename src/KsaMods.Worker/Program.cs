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

// Pinned by digest, never by tag: a tag is mutable, and "the image we tested" is the whole point
// of pinning. Without it the worker refuses to start rather than falling back to something
// convenient - a validator running an unknown image is worse than a validator not running.
var image = builder.Configuration["Validator:ImageDigest"];

if (string.IsNullOrWhiteSpace(image))
{
    Console.Error.WriteLine(
        "FATAL: set Validator__ImageDigest to the validator image, pinned by digest "
        + "(ksamods/validator@sha256:...). Refusing to start without it.");
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
builder.Services.AddHostedService<JobPump>();

var host = builder.Build();

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Worker")
    .LogInformation("Validator image {Image}, scratch {Scratch}.", image, scratch);

await host.RunAsync();

return 0;
