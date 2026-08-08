using System.Text.Json;
using System.Text.Json.Serialization;
using KsaMods.Metadata;
using KsaMods.Tests.Fixtures;
using KsaMods.Validation;
using KsaMods.Worker;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Runs a real archive through the real container.
///
/// <para>This is the seam nothing else covers. The pipeline is tested in-process and the container
/// flags are tested as a list of strings, but the two meet over a JSON file written by one process
/// and read by another - and a naming-policy mismatch there produces an empty report rather than
/// an error. The worker would record every import as having no findings and no facts, and every
/// test would still pass.</para>
///
/// <para>Skipped unless <c>KSAMODS_TEST_VALIDATOR_IMAGE</c> names an image, because it needs Docker
/// and a built validator.</para>
/// </summary>
public sealed class ValidatorContainerTests
{
    private static string Image => Environment.GetEnvironmentVariable("KSAMODS_TEST_VALIDATOR_IMAGE") ?? "";

    [RequiresValidatorFact]
    public async Task A_well_formed_mod_passes_and_its_facts_come_back()
    {
        var path = await WriteTempAsync(ModArchives.ContentMod());

        try
        {
            var runner = new ContainerRunner(new ContainerPolicy { ImageDigest = Image });
            var outcome = await runner.RunAsync(path, ModArchives.ContentModId, TestContext());

            Assert.False(outcome.TimedOut, outcome.Stderr);
            Assert.NotNull(outcome.ReportJson);

            var report = JsonSerializer.Deserialize<Report>(outcome.ReportJson!, Options);

            Assert.NotNull(report);

            // The exact thing a silent contract break would hide: a report that parsed into an
            // object with every field at its default.
            Assert.NotEqual("crashed", report.Outcome);
            Assert.NotNull(report.Facts);

            // Stage 6, which nothing consumes until much later and which cannot be backfilled once
            // the download rots. If it arrives empty the collision index is quietly worthless.
            Assert.NotEmpty(report.Facts!.AssetIds);
            Assert.Contains(report.Facts.AssetIds, a => a.Id == "OuterPlanets_Persephone");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [RequiresValidatorFact]
    public async Task A_broken_archive_comes_back_as_a_verdict_rather_than_a_crash()
    {
        // A mod whose id does not match its folder. The worker treats "failed" as a result to
        // record and "crashed" as our bug, so which one this is decides whether an author gets a
        // finding or a support ticket.
        var path = await WriteTempAsync(ModArchives.MissingDeclaredPath());

        try
        {
            var runner = new ContainerRunner(new ContainerPolicy { ImageDigest = Image });
            var outcome = await runner.RunAsync(path, ModArchives.ContentModId, TestContext());

            Assert.NotNull(outcome.ReportJson);

            var report = JsonSerializer.Deserialize<Report>(outcome.ReportJson!, Options);

            Assert.NotNull(report);
            Assert.NotEqual("crashed", report.Outcome);
            Assert.NotEmpty(report.Findings);

            // Findings have to survive the trip with their codes intact: they are the contract the
            // listing page and the CLI both render.
            Assert.All(report.Findings, f => Assert.StartsWith("KSAM-", f.Code, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CancellationToken TestContext() =>
        new CancellationTokenSource(TimeSpan.FromMinutes(3)).Token;

    private static async Task<string> WriteTempAsync(MemoryStream archive)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ksamods-test-{Guid.NewGuid():N}.zip");
        await File.WriteAllBytesAsync(path, archive.ToArray());
        return path;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private sealed record Report
    {
        [JsonPropertyName("outcome")] public string Outcome { get; init; } = "";
        [JsonPropertyName("error")] public string? Error { get; init; }
        [JsonPropertyName("findings")] public IReadOnlyList<Finding> Findings { get; init; } = [];
        [JsonPropertyName("facts")] public ExtractedFacts? Facts { get; init; }
    }
}

/// <summary>Skips itself unless Docker and a built validator image are available.</summary>
public sealed class RequiresValidatorFactAttribute : FactAttribute
{
    public const string Variable = "KSAMODS_TEST_VALIDATOR_IMAGE";

    public RequiresValidatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to a built validator image to run this.";
        }
    }
}
