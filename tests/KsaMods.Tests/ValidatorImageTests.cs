using KsaMods.Worker;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Choosing the image every validation runs in.
///
/// <para>The refusals matter more than the successes: a worker that starts without a usable
/// validator does not fail, it silently imports nothing, and the only symptom is listings that
/// never gain releases.</para>
/// </summary>
public class ValidatorImageTests
{
    private static async Task<(string? Image, string Log)> ResolveAsync(string? digest, string? tag)
    {
        var log = new System.Text.StringBuilder();
        var image = await ValidatorImage.ResolveAsync(digest, tag, m => log.AppendLine(m), CancellationToken.None);

        return (image, log.ToString());
    }

    [Fact]
    public async Task An_explicit_digest_is_used_exactly_as_given()
    {
        // Never resolved locally: somebody who pinned a digest meant that image, and looking the
        // name up on this host could quietly substitute a different one.
        const string pinned = "ksamods/validator@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var (image, _) = await ResolveAsync(pinned, tag: "ignored:latest");

        Assert.Equal(pinned, image);
    }

    [Fact]
    public async Task A_tag_in_the_digest_setting_is_refused_rather_than_accepted_quietly()
    {
        // Otherwise the setting named "ImageDigest" silently stops pinning anything, which is the
        // failure this whole mechanism exists to prevent.
        var (image, log) = await ResolveAsync("ksamods/validator:latest", tag: null);

        Assert.Null(image);
        Assert.Contains("Validator__Image", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_image_at_all_is_a_refusal_with_something_to_do_about_it()
    {
        var (image, log) = await ResolveAsync(digest: null, tag: null);

        Assert.Null(image);
        Assert.Contains("Validator__Image", log, StringComparison.Ordinal);
        Assert.Contains("Validator__ImageDigest", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_image_that_does_not_exist_is_a_refusal_rather_than_a_worker_that_fails_every_job()
    {
        // Failing at startup is the point. Starting successfully and dying on every job puts the
        // problem in a dead-letter queue an hour later instead of in the deploy log now.
        var (image, log) = await ResolveAsync(
            digest: null, tag: $"ksamods-does-not-exist-{Guid.NewGuid():N}:latest");

        Assert.Null(image);
        Assert.Contains("Could not find or pull", log, StringComparison.Ordinal);
    }

    [RequiresValidatorFact]
    public async Task A_tag_resolves_to_the_id_it_points_at()
    {
        // The property being bought: the worker runs an id, not a name, so a later push to the
        // same tag cannot change what it executes mid-life.
        var tag = Environment.GetEnvironmentVariable(RequiresValidatorFactAttribute.Variable)!;

        var (image, log) = await ResolveAsync(digest: null, tag);

        Assert.NotNull(image);
        Assert.StartsWith("sha256:", image, StringComparison.Ordinal);
        Assert.Contains("resolved to", log, StringComparison.Ordinal);
    }
}
