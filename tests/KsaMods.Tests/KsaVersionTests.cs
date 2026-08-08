using KsaMods.Metadata;
using Xunit;

namespace KsaMods.Tests;

public class KsaVersionTests
{
    [Theory]
    [InlineData("2026.8.3.5117", 2026, 8, 3, 5117, null)]
    [InlineData("v2026.8.3.5117", 2026, 8, 3, 5117, null)]
    [InlineData("2026.8.3.5117+6b87889f", 2026, 8, 3, 5117, null)]
    [InlineData("2026.8.3.5117-LOCAL", 2026, 8, 3, 5117, "LOCAL")]
    [InlineData("2026.8.3.5117-LOCAL+abc", 2026, 8, 3, 5117, "LOCAL")]
    public void Parses_the_shapes_the_game_itself_accepts(
        string input, int year, int month, int build, int revision, string? suffix)
    {
        Assert.True(KsaVersion.TryParse(input, out var version));
        Assert.Equal(year, version.Year);
        Assert.Equal(month, version.Month);
        Assert.Equal(build, version.Build);
        Assert.Equal(revision, version.Revision);
        Assert.Equal(suffix, version.Suffix);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026.8.3")]
    [InlineData("2026.8.3.5117.1")]
    [InlineData("not a version")]
    [InlineData("2026.8.x.5117")]
    public void Anything_that_does_not_parse_yields_no_version(string input)
    {
        // RFC 0017: whatever carried it becomes Unknown rather than being discarded.
        Assert.False(KsaVersion.TryParse(input, out _));
    }

    /// <summary>
    /// The oracle from RFC 0017 and research/ksa-versioning.md.
    ///
    /// <para>These are real adjacent pairs from the shipped history where a naive four-part
    /// comparison gets the order backwards. Sorting the full string puts 21 such pairs wrong;
    /// sorting by revision alone reproduces the true order for all 155 releases. If someone
    /// "fixes" the comparator to compare all four components, these fail.</para>
    /// </summary>
    [Theory]
    // (older by naive sort, but actually newer) , (newer by naive sort, but actually older)
    [InlineData("2025.8.24.2263", "2025.8.33.2091")]
    [InlineData("2025.9.2.2383", "2025.9.3.2279")]
    [InlineData("2025.9.3.2404", "2025.9.4.2290")]
    public void Orders_by_revision_alone_not_by_the_dotted_string(string actuallyNewer, string actuallyOlder)
    {
        var newer = KsaVersion.Parse(actuallyNewer);
        var older = KsaVersion.Parse(actuallyOlder);

        Assert.True(newer.CompareTo(older) > 0,
            $"{actuallyNewer} must sort above {actuallyOlder}: the third component is a machine-local " +
            "build counter and must never participate in ordering.");

        // And confirm the naive comparison really does disagree, so this test keeps its meaning.
        var naive = CompareAllFourComponents(actuallyNewer, actuallyOlder);
        Assert.True(naive < 0, "This pair no longer demonstrates the naive-sort trap.");
    }

    [Fact]
    public void Build_counter_breaks_ties_only_when_revisions_are_equal()
    {
        // Mirrors KSA.VersionInfo.CompareTo: revision, then build counter, then suffix.
        var a = KsaVersion.Parse("2026.8.9.5117");
        var b = KsaVersion.Parse("2026.8.3.5117");
        Assert.True(a.CompareTo(b) > 0);
    }

    [Fact]
    public void Year_and_month_never_participate_in_ordering()
    {
        // A version stamped with a later calendar month but a lower revision is still older.
        var lowerRevisionLaterMonth = KsaVersion.Parse("2026.12.1.5000");
        var higherRevisionEarlierMonth = KsaVersion.Parse("2026.1.1.5001");
        Assert.True(higherRevisionEarlierMonth.CompareTo(lowerRevisionLaterMonth) > 0);
    }

    [Fact]
    public void Display_string_keeps_the_build_counter()
    {
        // Meaningless for ordering, but it is what the user sees in-game and in a bug report,
        // and rendering something else invites confusion.
        Assert.Equal("2026.8.3.5117", KsaVersion.Parse("v2026.8.3.5117+deadbeef").ToDisplayString());
    }

    [Theory]
    [InlineData("2026.7", 2026, 7)]
    [InlineData("v2026.12", 2026, 12)]
    public void Parses_a_month_bound(string input, int year, int month)
    {
        Assert.True(KsaVersion.TryParseMonth(input, out var parsed));
        Assert.Equal((year, month), parsed);
    }

    [Theory]
    [InlineData("2026.13")]
    [InlineData("2026.0")]
    [InlineData("2026.8.3")]
    public void Rejects_a_month_that_is_not_a_month(string input)
    {
        Assert.False(KsaVersion.TryParseMonth(input, out _));
    }

    private static int CompareAllFourComponents(string a, string b)
    {
        var x = a.Split('.').Select(int.Parse).ToArray();
        var y = b.Split('.').Select(int.Parse).ToArray();
        for (var i = 0; i < 4; i++)
        {
            var c = x[i].CompareTo(y[i]);
            if (c != 0) return c;
        }
        return 0;
    }
}

public class CompatibilityTests
{
    [Theory]
    [InlineData(5117, null, null, CompatibilityState.Unknown)]
    [InlineData(5000, 5117, null, CompatibilityState.Incompatible)]
    [InlineData(5117, 5117, null, CompatibilityState.Compatible)]
    [InlineData(9999, 5117, null, CompatibilityState.Compatible)]
    [InlineData(5117, 5000, 5200, CompatibilityState.Compatible)]
    [InlineData(5201, 5000, 5200, CompatibilityState.Untested)]
    public void Evaluates_the_four_states(int installed, int? min, int? max, CompatibilityState expected)
    {
        Assert.Equal(expected, Compatibility.Evaluate(installed, min, max));
    }

    [Fact]
    public void Only_incompatible_blocks()
    {
        // The game validates nothing, so a false "incompatible" takes a working mod away for no
        // reason while a false "compatible" is a mod that can simply be removed again.
        Assert.True(CompatibilityState.Incompatible.Blocks());
        Assert.False(CompatibilityState.Untested.Blocks());
        Assert.False(CompatibilityState.Unknown.Blocks());
        Assert.False(CompatibilityState.Compatible.Blocks());
    }
}
