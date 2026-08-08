using KsaMods.Metadata;
using Xunit;

namespace KsaMods.Tests;

public class SemVerTests
{
    [Theory]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("v1.2.0", "1.2.0")]           // a leading v on a forge tag is stripped
    [InlineData("0.7.0-beta.1", "0.7.0-beta.1")]
    [InlineData("1.0.0+build.5", "1.0.0+build.5")]
    public void Normalises_a_forge_tag(string input, string expected)
    {
        Assert.True(SemVer.TryParse(input, out var v));
        Assert.Equal(expected, v.Normalised);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("release-2026")]
    [InlineData("")]
    public void Rejects_what_is_not_semver(string input)
    {
        // RFC 0031: a version that does not parse rejects the release at import time with the
        // error in front of the author, rather than being coerced into something plausible.
        Assert.False(SemVer.TryParse(input, out _));
    }

    [Fact]
    public void Orders_by_semver_precedence_including_prerelease()
    {
        var ordered = new[]
        {
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0",
        }.Select(SemVer.Parse).ToArray();

        for (var i = 1; i < ordered.Length; i++)
        {
            Assert.True(ordered[i - 1] < ordered[i],
                $"{ordered[i - 1]} should sort below {ordered[i]}");
        }
    }

    [Fact]
    public void Numeric_prerelease_identifiers_compare_numerically()
    {
        // The case that bites anyone sorting these as text: alpha.10 is above alpha.2.
        Assert.True(SemVer.Parse("1.0.0-alpha.10") > SemVer.Parse("1.0.0-alpha.2"));
        Assert.True(SemVer.Parse("1.0.0-alpha.2") > SemVer.Parse("1.0.0-alpha"));
    }

    [Fact]
    public void Build_metadata_is_ignored_for_precedence()
    {
        Assert.Equal(SemVer.Parse("1.0.0+a"), SemVer.Parse("1.0.0+b"));
    }

    /// <summary>
    /// The sort key must order identically to CompareTo under plain byte comparison, because
    /// Postgres orders <c>bytea</c> that way and backend.md §3.1 relies on it to avoid a
    /// function call per row.
    /// </summary>
    [Fact]
    public void Sort_key_byte_order_matches_comparer_order()
    {
        var versions = new[]
        {
            "0.0.1", "0.1.0", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.2", "1.0.0-alpha.10",
            "1.0.0-beta", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.2.0", "2.0.0", "10.0.0",
        }.Select(SemVer.Parse).ToArray();

        for (var i = 0; i < versions.Length; i++)
        {
            for (var j = 0; j < versions.Length; j++)
            {
                var bySemVer = Math.Sign(versions[i].CompareTo(versions[j]));
                var byKey = Math.Sign(CompareBytes(versions[i].ToSortKey(), versions[j].ToSortKey()));

                Assert.True(bySemVer == byKey,
                    $"{versions[i]} vs {versions[j]}: comparer says {bySemVer}, sort key says {byKey}");
            }
        }
    }

    [Fact]
    public void Sort_key_places_every_prerelease_below_its_release()
    {
        var pre = SemVer.Parse("1.0.0-zzz.999").ToSortKey();
        var release = SemVer.Parse("1.0.0").ToSortKey();
        Assert.True(CompareBytes(pre, release) < 0);
    }

    private static int CompareBytes(byte[] a, byte[] b)
    {
        var shared = Math.Min(a.Length, b.Length);
        for (var i = 0; i < shared; i++)
        {
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        }
        return a.Length.CompareTo(b.Length);
    }
}
