using System.Text.Json;
using System.Text.Json.Serialization;
using KsaMods.Metadata;
using KsaMods.Validation;

// The process that runs inside the validation container (backend.md §7.1).
//
// Contract, kept deliberately tiny because this is the process handling attacker-controlled
// bytes:
//
//   /in/archive.zip   read-only mount, supplied by the worker
//   /out/report.json  the only thing written
//   argv[0]           the expected mod id
//
// It has no network (--network none), no capabilities, a read-only root filesystem and runs as
// nobody. It never opens a socket, never resolves a name, and never writes outside /out.

var inputPath = Environment.GetEnvironmentVariable("KSAMODS_INPUT") ?? "/in/archive.zip";
var outputPath = Environment.GetEnvironmentVariable("KSAMODS_OUTPUT") ?? "/out/report.json";

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

if (!File.Exists(inputPath))
{
    await Console.Error.WriteLineAsync($"input archive not found at {inputPath}");
    return ExitCodes.Usage;
}

ValidationResult result;
try
{
    await using var archive = File.OpenRead(inputPath);
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
