using KsaMods.Web.Services;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// These assert wording as much as logic, deliberately. The site's whole product is trust, and a
/// pill that says "Verified" when the bytes changed is a lie the user has no way to detect.
/// </summary>
public class CompatibilityPresentationTests
{
    [Fact]
    public void No_declared_minimum_reads_as_unknown_not_as_compatible()
    {
        var pill = Presentation.Compatibility(installedRevision: 5117, min: null, max: null);

        Assert.Equal("Unknown", pill.Label);
        Assert.Equal("outline", pill.Variant);
    }

    [Fact]
    public void Below_the_minimum_is_the_only_state_that_reads_as_a_refusal()
    {
        var incompatible = Presentation.Compatibility(5000, 5117, null);
        Assert.Equal("Incompatible", incompatible.Label);
        Assert.Equal("error", incompatible.Variant);

        // Untested must not read as a block: the game validates nothing, and a false
        // "incompatible" takes a working mod away from the user for no reason.
        var untested = Presentation.Compatibility(5300, 5000, 5200);
        Assert.Equal("Untested", untested.Label);
        Assert.Equal("warning", untested.Variant);
        Assert.Contains("might still work", untested.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_open_upper_bound_stays_compatible()
    {
        var pill = Presentation.Compatibility(99999, 5000, null);
        Assert.Equal("Compatible", pill.Label);
    }

    [Fact]
    public void Bounds_are_inclusive_at_both_ends()
    {
        Assert.Equal("Compatible", Presentation.Compatibility(5000, 5000, 5200).Label);
        Assert.Equal("Compatible", Presentation.Compatibility(5200, 5000, 5200).Label);
        Assert.Equal("Untested", Presentation.Compatibility(5201, 5000, 5200).Label);
    }

    [Fact]
    public void An_unknown_installed_build_states_the_requirement_rather_than_guessing()
    {
        var pill = Presentation.Compatibility(installedRevision: null, min: 5117, max: null);

        Assert.Contains("5117", pill.Label, StringComparison.Ordinal);
        Assert.NotEqual("Compatible", pill.Label);
        Assert.NotEqual("Incompatible", pill.Label);
    }
}

public class AvailabilityPresentationTests
{
    [Fact]
    public void Diverged_says_the_bytes_changed_and_never_says_verified()
    {
        var pill = Presentation.Availability("diverged", null);

        Assert.DoesNotContain("Verified", pill.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("warning", pill.Variant);
        Assert.Contains("isn't the one we imported", pill.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Quarantined_reads_as_an_error_and_explains_what_changed()
    {
        var pill = Presentation.Availability("quarantined", null);

        Assert.Equal("error", pill.Variant);
        Assert.Contains("different assemblies", pill.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unverified_does_not_claim_a_check_that_never_happened()
    {
        var pill = Presentation.Availability("unverified", null);

        Assert.Contains("Not yet verified", pill.Label, StringComparison.Ordinal);
        Assert.NotEqual("ok", pill.Variant);
    }

    [Fact]
    public void A_dead_link_keeps_the_record_and_says_why()
    {
        var pill = Presentation.Availability("unavailable", null);

        Assert.Equal("error", pill.Variant);
        Assert.Contains("modlists", pill.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verified_carries_when_it_was_checked()
    {
        var checkedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var pill = Presentation.Availability("verified", checkedAt);

        Assert.Contains("3d ago", pill.Label, StringComparison.Ordinal);
        Assert.Equal("ok", pill.Variant);
    }
}

public class ValidationPresentationTests
{
    [Fact]
    public void A_failed_release_explains_that_it_is_maintainer_only()
    {
        var pill = Presentation.Validation("failed");

        Assert.Equal("error", pill.Variant);
        Assert.Contains("maintainers", pill.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Warnings_do_not_read_as_a_block()
    {
        var pill = Presentation.Validation("passed_warnings");

        Assert.Equal("warning", pill.Variant);
        Assert.Contains("Installable", pill.Explanation, StringComparison.OrdinalIgnoreCase);
    }
}

public class FormattingTests
{
    [Theory]
    [InlineData(null, "-")]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KiB")]
    [InlineData(1536L, "1.5 KiB")]
    [InlineData(1048576L, "1 MiB")]
    [InlineData(1073741824L, "1 GiB")]
    public void Formats_sizes_in_binary_units(long? bytes, string expected)
    {
        Assert.Equal(expected, Presentation.Bytes(bytes));
    }

    [Fact]
    public void Relative_time_never_reads_as_the_future()
    {
        // Clock skew between the API host and this one must not produce "in 3 minutes".
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("just now", Presentation.Ago(now.AddMinutes(5), now));
    }

    [Theory]
    [InlineData(30, "30m ago")]
    [InlineData(60 * 5, "5h ago")]
    [InlineData(60 * 24 * 3, "3d ago")]
    [InlineData(60 * 24 * 45, "1mo ago")]
    [InlineData(60 * 24 * 400, "1y ago")]
    public void Relative_time_picks_a_sensible_unit(int minutesAgo, string expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, Presentation.Ago(now.AddMinutes(-minutesAgo), now));
    }

    [Fact]
    public void Short_hash_keeps_both_ends_so_a_comparison_is_still_possible()
    {
        var hash = new string('a', 32) + new string('b', 32);
        var shortened = Presentation.ShortHash(hash);

        Assert.StartsWith("aaaaaaaa", shortened, StringComparison.Ordinal);
        Assert.EndsWith("bbbbbbbb", shortened, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage_names_cover_every_pipeline_stage()
    {
        // A finding that says "Stage 5" instead of "Declared content" makes the reader open the
        // spec to understand their own error.
        foreach (var stage in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            Assert.DoesNotContain("Stage", Presentation.StageName(stage), StringComparison.Ordinal);
        }
    }
}

