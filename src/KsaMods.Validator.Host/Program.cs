using System.Text.Json;
using System.Text.Json.Serialization;
using KsaMods.Metadata;
using KsaMods.Validation;

// The process that runs inside the validation container (backend.md §7.1).
//
// Contract, kept deliberately tiny because this is the process handling attacker-controlled
// bytes:
//
//   stdin             the archive, or a read-only mount when KSAMODS_INPUT names a path
//   stdout            the report, or a file when KSAMODS_OUTPUT names one
//   argv[0]           the expected mod id
//
// Pipes by default, and that is not a style choice. The worker itself runs in a container, so a
// path it writes is a path the Docker daemon cannot see: the daemon resolves bind mounts on the
// host, finds nothing there, and helpfully creates an empty directory - so /in/archive.zip arrives
// as a directory and the run fails with a message about a missing file. Piping removes the shared
// filesystem entirely, which also means this process has no writable mount at all.
//
// It has no network (--network none), no capabilities, a read-only root filesystem and runs as
// nobody. It never opens a socket, never resolves a name, and never writes outside /out.

// "-" means the pipe. Paths still work, because the CLI runs this same code against a file.
var inputPath = Environment.GetEnvironmentVariable("KSAMODS_INPUT") ?? "-";
var outputPath = Environment.GetEnvironmentVariable("KSAMODS_OUTPUT") ?? "-";

// A run that does nothing but prove the image works. The deployment builds this image as a
// one-shot compose service, and a service that exits non-zero is a failed deploy - so there has to
// be something it can be asked to do that succeeds without an archive.
if (args.Contains("--version"))
{
    Console.WriteLine("ksamods validator, report schema 1");
    return ExitCodes.Ok;
}

if (args.Length < 1)
{
    await Console.Error.WriteLineAsync("usage: KsaMods.Validator.Host <expected-mod-id> [core-asset-ids.txt]");
    return ExitCodes.Usage;
}

var expectedId = args[0];

IReadOnlySet<string>? coreAssetIds = null;
if (args.Length > 1 && File.Exists(args[1]))
{
    coreAssetIds = new HashSet<string>(await File.ReadAllLinesAsync(args[1]), StringComparer.Ordinal);
}

var fromStdin = inputPath == "-";

if (!fromStdin && !File.Exists(inputPath))
{
    await Console.Error.WriteLineAsync($"input archive not found at {inputPath}");
    return ExitCodes.Usage;
}

ValidationResult result;
try
{
    await using var archive = fromStdin ? Console.OpenStandardInput() : File.OpenRead(inputPath);
    using var buffer = new MemoryStream();
    await archive.CopyToAsync(buffer);
    buffer.Position = 0;

    result = ValidationPipeline.Validate(new ValidationRequest
    {
        Archive = buffer,
        ExpectedId = expectedId,
        CoreAssetIds = coreAssetIds,
    });
}
catch (Exception ex)
{
    // A crash here is a bug in the validator, not a verdict about the mod. Report it as such
    // rather than letting the worker record a spurious "failed" against the author's release.
    await WriteReportAsync(outputPath, new Report
    {
        Outcome = "crashed",
        Error = $"{ex.GetType().Name}: {ex.Message}",
    });
    return ExitCodes.ValidatorFault;
}

await WriteReportAsync(outputPath, Report.From(result));

// The exit code says whether the validator ran, not whether the mod passed. A failing mod is a
// successful run: the worker reads the verdict from the report.
return ExitCodes.Ok;

static async Task WriteReportAsync(string path, Report report)
{
    // Nothing else is ever written to stdout, so the whole stream is the report and the worker
    // can parse it without hunting for a delimiter.
    if (path == "-")
    {
        await using var stdout = Console.OpenStandardOutput();
        await JsonSerializer.SerializeAsync(stdout, report, ReportJson.Options);
        return;
    }

    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

    await using var stream = File.Create(path);
    await JsonSerializer.SerializeAsync(stream, report, ReportJson.Options);
}

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 2;
    public const int ValidatorFault = 3;
}

internal sealed record Report
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;
    [JsonPropertyName("outcome")] public required string Outcome { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("findings")] public IReadOnlyList<Finding> Findings { get; init; } = [];
    [JsonPropertyName("facts")] public ExtractedFacts? Facts { get; init; }

    public static Report From(ValidationResult result) => new()
    {
        Outcome = result.Outcome switch
        {
            ValidationOutcome.Passed => "passed",
            ValidationOutcome.PassedWithWarnings => "passed_warnings",
            _ => "failed",
        },
        Findings = result.Findings,
        Facts = result.Facts,
    };
}

internal static class ReportJson
{
    /// <summary>
    /// The worker/container contract. snake_case throughout to match the RFC 0031 documents this
    /// data eventually becomes, so one naming convention holds from the container to the export.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
}
