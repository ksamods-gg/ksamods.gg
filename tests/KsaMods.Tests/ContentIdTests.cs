using KsaMods.Metadata;
using Xunit;

namespace KsaMods.Tests;

public class ContentIdTests
{
    [Theory]
    [InlineData("AdvancedFlightComputer")]
    [InlineData("AircraftHUD")]
    [InlineData("author.coolmod")]
    [InlineData("a")]
    [InlineData("Kitten_Extensions")]
    [InlineData("outer-planets")]
    [InlineData("Mod2")]
    public void Accepts_ids_the_existing_corpus_actually_uses(string value)
    {
        // An earlier draft of the standard mandated lowercase <author>.<mod>, which would have
        // excluded every mod that exists. RFC 0031's format admits these directly.
        Assert.True(ContentId.TryParse(value, out _, out var reason), $"rejected: {reason}");
    }

    [Theory]
    [InlineData("", IdRejection.Empty)]
    [InlineData(".hidden", IdRejection.Boundary)]      // leading dot hides the folder on Unix
    [InlineData("trailing.", IdRejection.Boundary)]    // Windows silently strips a trailing dot
    [InlineData("-leading", IdRejection.Boundary)]
    [InlineData("trailing-", IdRejection.Boundary)]
    [InlineData("has space", IdRejection.Charset)]     // Windows rejects or strips these
    [InlineData("has\"quote", IdRejection.Charset)]    // ModManifest.Save writes ids unescaped
    [InlineData("naïve", IdRejection.Charset)]         // macOS normalises non-ASCII differently
    [InlineData("slash/inside", IdRejection.Charset)]
    public void Rejects_what_would_break_a_filesystem_or_the_manifest(string value, IdRejection expected)
    {
        Assert.False(ContentId.TryParse(value, out _, out var reason));
        Assert.Equal(expected, reason);
    }

    [Fact]
    public void Rejects_ids_longer_than_64_characters()
    {
        // 64 because the id lands in real paths under Documents and Windows caps a path at 260.
        Assert.True(ContentId.TryParse(new string('a', 64), out _, out _));
        Assert.False(ContentId.TryParse(new string('a', 65), out _, out var reason));
        Assert.Equal(IdRejection.Length, reason);
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("core")]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Reserves_Core_and_the_windows_device_names(string value)
    {
        Assert.False(ContentId.TryParse(value, out _, out var reason));
        Assert.Equal(IdRejection.Reserved, reason);
    }

    [Theory]
    [InlineData("CON.mod")]
    [InlineData("nul.something")]
    [InlineData("Core.extras")]
    public void Checks_reserved_names_up_to_the_first_dot(string value)
    {
        // Windows treats dotted forms such as CON.mod as devices too, so checking the whole
        // string misses them. This is the case a naive implementation gets wrong.
        Assert.False(ContentId.TryParse(value, out _, out var reason));
        Assert.Equal(IdRejection.Reserved, reason);
    }

    [Theory]
    [InlineData("Console")]
    [InlineData("Corelib")]
    [InlineData("COM10")]
    [InlineData("NULled")]
    public void Does_not_over_reserve(string value)
    {
        // Only the exact device names are reserved, not anything starting with one.
        Assert.True(ContentId.TryParse(value, out _, out var reason), $"rejected: {reason}");
    }

    [Fact]
    public void Compares_case_insensitively_but_preserves_authored_casing()
    {
        // Folder names are case-insensitive on Windows and case-sensitive on Linux, so MyMod and
        // mymod would be one mod on one machine and two on another.
        var a = ContentId.Parse("MyMod");
        var b = ContentId.Parse("mymod");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal("MyMod", a.Value);
        Assert.Equal("mymod", b.Value);
        Assert.Equal("mymod", a.Lower);
    }

    [Fact]
    public void Accepted_ids_always_round_trip_through_the_regex_in_the_spec()
    {
        // Property check over the full single- and double-character space plus a fuzzed sample,
        // asserting the implementation agrees with RFC 0031's stated regex.
        var pattern = new System.Text.RegularExpressions.Regex(
            @"^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$");

        var alphabet = "Aa0._-\" /".ToCharArray();
        var random = new Random(20260808);

        for (var i = 0; i < 20_000; i++)
        {
            var length = random.Next(1, 8);
            var candidate = string.Concat(Enumerable.Range(0, length)
                .Select(_ => alphabet[random.Next(alphabet.Length)]));

            var accepted = ContentId.TryParse(candidate, out _, out _);
            var matchesRegex = pattern.IsMatch(candidate) && !ContentId.IsReserved(candidate);

            Assert.True(accepted == matchesRegex,
                $"'{candidate}': implementation says {accepted}, spec regex says {matchesRegex}");
        }
    }
}
