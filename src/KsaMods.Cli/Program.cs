using KsaMods.Metadata;
using KsaMods.Validation;

// ksamods validate - the same rules the server runs, in front of the author.
//
// This exists so a packaging mistake is caught before a release is tagged rather than after it
// is imported. It shares KsaMods.Validation with the container, so "the CLI and the server agree"
// is true by construction rather than by discipline.

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("""
        ksamods - KSA mod archive validator

        usage:
          ksamods validate <archive.zip> [--id <mod-id>] [--quiet]

        The mod id defaults to the archive's single top-level directory. Exit codes:
          0  passed, or passed with warnings
          1  failed validation
          2  usage error
        """);
    return args.Length == 0 ? 2 : 0;
}

if (args[0] != "validate")
{
    await Console.Error.WriteLineAsync($"unknown command '{args[0]}'. Try 'ksamods --help'.");
    return 2;
}

if (args.Length < 2)
{
    await Console.Error.WriteLineAsync("usage: ksamods validate <archive.zip> [--id <mod-id>]");
    return 2;
}

var archivePath = args[1];
string? explicitId = null;
var quiet = false;

for (var i = 2; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--id" when i + 1 < args.Length:
            explicitId = args[++i];
            break;
        case "--quiet":
            quiet = true;
            break;
        default:
            await Console.Error.WriteLineAsync($"unknown option '{args[i]}'.");
            return 2;
    }
}

if (!File.Exists(archivePath))
{
    await Console.Error.WriteLineAsync($"no such file: {archivePath}");
    return 2;
}

await using var file = File.OpenRead(archivePath);
using var buffer = new MemoryStream();
await file.CopyToAsync(buffer);

// Default the id to whatever the archive's own root directory is, so the common case needs no
// flag. Passing --id is how you check the archive against the id you actually claimed.
var expectedId = explicitId ?? InferRootDirectory(buffer);
buffer.Position = 0;

if (expectedId is null)
{
    await Console.Error.WriteLineAsync(
        "could not infer the mod id from the archive; pass --id explicitly.");
    return 2;
}

var result = ValidationPipeline.Validate(new ValidationRequest
{
    Archive = buffer,
    ExpectedId = expectedId,
});

if (!quiet) PrintReport(result, expectedId);

return result.Outcome == ValidationOutcome.Failed ? 1 : 0;

static string? InferRootDirectory(MemoryStream archive)
{
    archive.Position = 0;
    using var probe = SafeArchive.Open(archive);

    var roots = probe.Entries
        .Select(e => e.Path.Split('/', 2)[0])
        .Where(s => s.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .Take(2)
        .ToList();

    return roots.Count == 1 ? roots[0] : null;
}

static void PrintReport(ValidationResult result, string expectedId)
{
    var errors = result.Findings.Count(f => f.Severity == Severity.Error);
    var warnings = result.Findings.Count(f => f.Severity == Severity.Warning);

    foreach (var finding in result.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Code))
    {
        var label = finding.Severity switch
        {
            Severity.Error => "error",
            Severity.Warning => "warning",
            _ => "note",
        };

        var location = finding.Path is null ? "" : $" [{finding.Path}]";
        Console.WriteLine($"{label} {finding.Code}: {finding.Message}{location}");
    }

    if (result.Findings.Count > 0) Console.WriteLine();

    var verdict = result.Outcome switch
    {
        ValidationOutcome.Passed => "passed",
        ValidationOutcome.PassedWithWarnings => "passed with warnings",
        _ => "FAILED",
    };

    Console.WriteLine($"{expectedId}: {verdict} ({errors} error(s), {warnings} warning(s))");

    if (result.Outcome == ValidationOutcome.Failed) return;

    var facts = result.Facts;
    Console.WriteLine(
        $"  {facts.AssetIds.Count} asset id(s), {facts.Dependencies.Count} declared dependency(ies), " +
        $"{facts.Assemblies.Count} assembly(ies), {facts.UncompressedSize / 1024} KiB unpacked");

    // Surfaced deliberately: this is the most direct scripted-behaviour vector in a mod.toml,
    // and an author should see it in their own terminal before a reviewer sees it in a queue.
    if (facts.ConsoleCommands.Count > 0)
    {
        Console.WriteLine($"  [console] runs {facts.ConsoleCommands.Count} command(s) automatically:");
        foreach (var command in facts.ConsoleCommands)
        {
            Console.WriteLine($"    {command.Hook}[{command.Ordinal}]: {command.Command}");
        }
    }
}
