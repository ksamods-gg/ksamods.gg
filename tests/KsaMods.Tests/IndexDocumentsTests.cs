using KsaMods.Metadata;
using KsaMods.Worker;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// Read against the real files from KSAModding/content-index and content-index-releases, copied
/// into IndexFixtures unmodified.
///
/// <para>Hand-written fixtures would test that our reader understands our idea of their format,
/// which is the thing most likely to be wrong. These are their bytes.</para>
/// </summary>
public class IndexDocumentsTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "IndexFixtures", name));

    [Fact]
    public void Reads_the_StarMap_listing_including_the_install_descriptor()
    {
        var document = IndexDocuments.ParseListing(Fixture("StarMap.toml"), out var error);

        Assert.Null(error);
        Assert.NotNull(document);
        Assert.Equal("StarMap", document.Id);
        Assert.Equal(ContentType.ModLoader, document.Type);
        Assert.Equal(["KlaasWhite"], document.Authors);
        Assert.Equal("MIT", document.License);

        // [releases] is what binds ownership upstream and tells a watcher where to look.
        Assert.Equal("StarMapLoader/StarMap", document.Releases?.GitHub);
        Assert.Equal("2026.8.3.5117", document.Compatibility?.GameMin);

        // RFC 0035. The kebab-cased keys are the ones a hand-written mapper gets wrong, so they
        // are the ones worth asserting: content-dir and game-path both map through the
        // JsonPropertyName attributes rather than through a naming convention.
        Assert.Equal(InstallAnchor.Standalone, document.Install?.Target);
        Assert.Single(document.Install!.Uninstall!);
        Assert.Equal("StarMap.exe", document.Provides?.Launch);
        Assert.Equal(InstallAnchor.Mods, document.Provides?.ContentDir);
        Assert.Equal("StarMapConfig.json", document.Provides?.Configure?.File);
        Assert.Equal("json", document.Provides?.Configure?.Format);
        Assert.Equal("GameLocation", document.Provides?.Configure?.GamePath);
    }

    [Fact]
    public void The_StarMap_descriptor_they_published_is_valid_by_our_own_rules()
    {
        // If this ever fails, either they changed something or our RFC 0035 checker is wrong.
        // Both are worth finding out from a test rather than from an empty catalogue.
        var document = IndexDocuments.ParseListing(Fixture("StarMap.toml"), out _)!;

        Assert.Null(InstallDescriptor.Check(document.Type, document.Install, document.Provides));
    }

    [Fact]
    public void Reads_a_mod_listing_and_its_loader_bound()
    {
        var document = IndexDocuments.ParseListing(Fixture("AdvancedFlightComputer.toml"), out var error);

        Assert.Null(error);
        Assert.Equal(ContentType.Mod, document!.Type);
        Assert.Equal("StarMap", document.Loader?.Id);
        Assert.Equal("0.4.5", document.Loader?.Min);
        Assert.Contains("control", document.Tags);
    }

    [Fact]
    public void Reads_a_stamped_release_whole()
    {
        var release = IndexDocuments.ParseRelease(Fixture("StarMap-0.4.6.json"), out var error);

        Assert.Null(error);
        Assert.Equal("StarMap", release!.Id);
        Assert.Equal("0.4.6", release.Version);
        Assert.Equal(5117, release.GameMinRevision);
        Assert.Equal(891020, release.Download.Size);
        Assert.Equal("application/zip", release.Download.ContentType);
        Assert.Equal(InstallAnchor.Standalone, release.Install?.Target);

        // The frozen listing block, which is what lets an old release be shown as it was described
        // when it shipped rather than as the listing reads today.
        Assert.Equal("StarMap", release.Listing?.Name);
        Assert.Equal(["KlaasWhite"], release.Listing?.Authors);
    }

    [Fact]
    public void A_release_hash_survives_the_round_trip_in_the_case_it_was_written_in()
    {
        // Theirs is uppercase hex. Ours is bytea, and the comparison that matters happens after
        // decoding, so this only has to not be mangled on the way through.
        var release = IndexDocuments.ParseRelease(Fixture("StarMap-0.4.6.json"), out _)!;

        Assert.Equal(64, release.Download.Sha256.Length);
        Assert.Matches("^[0-9a-fA-F]{64}$", release.Download.Sha256);
    }

    [Fact]
    public void An_empty_index_status_is_no_entries_rather_than_a_failure()
    {
        // Their file today is `entries = []` under thirty lines of comment. Nothing is wrong with
        // the index, so nothing should read as wrong.
        Assert.Empty(IndexDocuments.ParseStatus(Fixture("index-status.toml")));
    }

    [Fact]
    public void Reads_index_status_entries_when_there_are_some()
    {
        var entries = IndexDocuments.ParseStatus("""
            [[entries]]
            id = "SomeMod"
            state = "delisted"
            since = 2026-08-10T00:00:00Z
            reason = "Taken down at the author's request."

            [[entries]]
            id = "SomePack"
            state = "retracted"
            version = "1.2.0"
            """);

        Assert.Equal(2, entries.Count);
        Assert.Equal("delisted", entries[0].State);
        Assert.Equal("Taken down at the author's request.", entries[0].Reason);
        Assert.NotNull(entries[0].Since);

        // retracted scopes to one pack version, so the version is the part that must survive.
        Assert.Equal("1.2.0", entries[1].Version);
    }

    [Fact]
    public void Reads_game_versions_and_takes_the_revision_from_the_string()
    {
        var versions = IndexDocuments.ParseGameVersions(Fixture("game-versions.json"));

        Assert.NotEmpty(versions);

        // Their file carries no dates at all. The revision is the fourth component, and it is the
        // only component that orders - so a build list with no dates is still enough.
        Assert.Contains(versions, v => v is { Revision: 5348, Version: "2026.8.22.5348" });
        Assert.All(versions, v => Assert.True(v.Revision > 0));
    }

    [Fact]
    public void A_malformed_listing_costs_that_listing_and_not_the_sync()
    {
        Assert.Null(IndexDocuments.ParseListing("this is not = = toml", out var error));
        Assert.NotNull(error);

        Assert.Null(IndexDocuments.ParseRelease("{ not json", out var releaseError));
        Assert.NotNull(releaseError);

        // A release with no download is unusable rather than merely odd: the whole point of the
        // record is telling somebody where to get the file.
        Assert.Null(IndexDocuments.ParseRelease("""{"id":"X","version":"1.0.0"}""", out var missing));
        Assert.NotNull(missing);
    }
}
