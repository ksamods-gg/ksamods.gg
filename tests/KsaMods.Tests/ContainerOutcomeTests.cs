using KsaMods.Worker;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// The discriminator between "this release is bad" and "our plumbing is broken". Getting it wrong
/// is expensive in one direction: a misread infrastructure fault spends an author's retry budget
/// and writes our own error onto their listing.
/// </summary>
public class ContainerOutcomeTests
{
    private static ContainerOutcome Outcome(int exitCode, string stderr, bool timedOut = false) => new()
    {
        ExitCode = exitCode,
        TimedOut = timedOut,
        ReportJson = null,
        Stderr = stderr,
    };

    [Fact]
    public void A_missing_image_is_ours_not_the_release()
    {
        var outcome = Outcome(
            ContainerOutcome.DockerRefused,
            "docker: Error response from daemon: No such image: sha256:580165d90729");

        Assert.True(outcome.NeverStarted);
        Assert.True(outcome.ImageMissing);
    }

    [Fact]
    public void Docker_refusing_for_another_reason_is_not_a_missing_image()
    {
        // Still ours, still never ran, but a restart fixes nothing - so it must not be the one
        // that stops the worker.
        var outcome = Outcome(
            ContainerOutcome.DockerRefused,
            "docker: Error response from daemon: unknown flag: --seccomp");

        Assert.True(outcome.NeverStarted);
        Assert.False(outcome.ImageMissing);
    }

    [Fact]
    public void A_validator_that_ran_and_failed_is_the_releases_problem()
    {
        // The validator's own non-zero exit, and an archive that happens to mention the phrase.
        // Matching on stderr alone would hand a stranger a way to stop our worker by naming a
        // file "No such image" in their zip.
        var outcome = Outcome(1, "stage 4: No such image referenced in mod.toml");

        Assert.False(outcome.NeverStarted);
        Assert.False(outcome.ImageMissing);
    }

    [Fact]
    public void A_timeout_is_never_an_infrastructure_fault()
    {
        var outcome = Outcome(ContainerOutcome.DockerRefused, "No such image", timedOut: true);

        Assert.False(outcome.NeverStarted);
        Assert.False(outcome.ImageMissing);
    }
}
