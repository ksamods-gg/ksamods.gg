using KsaMods.Web.Services;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// The "what needs me" logic behind Your mods.
///
/// <para>Pure, and worth pinning: it decides which listings are put in front of an author as
/// theirs to act on. Getting it wrong in one direction hides a broken listing; in the other it
/// sends somebody to a button the API will refuse, which is the same as lying to them.</para>
/// </summary>
public class OwnedModStatusTests
{
    private static OwnedModStatus Mod(
        string role = "owner", string listingState = "listed",
        bool repoConnected = true, bool repoVerified = true,
        int releases = 1, int failed = 0, string? importError = null) => new()
        {
            Id = "Test.Mod",
            Role = role,
            ListingState = listingState,
            RepoConnected = repoConnected,
            RepoVerified = repoVerified,
            Releases = releases,
            FailedReleases = failed,
            LastImportError = importError,
        };

    [Fact]
    public void A_healthy_listing_asks_for_nothing()
    {
        var mod = Mod();

        Assert.Null(mod.NextStep);
        Assert.False(mod.NeedsAttention);
    }

    [Fact]
    public void The_first_blocking_step_is_the_one_shown()
    {
        // A listing with no repository also has no releases and is probably a draft. Listing all
        // three is a description of everything that is not yet true, not a next step.
        var fresh = Mod(repoConnected: false, repoVerified: false, releases: 0, listingState: "unlisted");

        Assert.Equal("Connect the repository you release from.", fresh.NextStep);
    }

    [Fact]
    public void An_unverified_repository_is_the_step_before_importing()
    {
        // Importing from an unproven link is refused by the API, so telling somebody to import is
        // sending them at a closed door.
        var mod = Mod(repoVerified: false, releases: 0);

        // Asserted as "does not send them to import" rather than on the exact wording, which is
        // the thing the test is actually about and survives the copy being rewritten.
        Assert.DoesNotContain("import", mod.NextStep!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prove", mod.NextStep!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_import_outranks_being_a_draft()
    {
        // Publishing a listing whose imports are failing publishes a listing with nothing in it.
        var mod = Mod(listingState: "unlisted", importError: "No .zip asset attached.");

        Assert.Equal("The last import failed.", mod.NextStep);
    }

    [Fact]
    public void A_withdrawn_listing_asks_nothing_of_its_author()
    {
        // The next step is a moderator's, and an author cannot clear a takedown. Offering them
        // work to do here would suggest otherwise.
        var mod = Mod(listingState: "delisted", repoConnected: false);

        Assert.Null(mod.NextStep);
        Assert.False(mod.NeedsAttention);
    }

    [Fact]
    public void A_maintainer_is_not_asked_to_do_the_owners_job()
    {
        // Connecting a repository is owner-only (§4.2). A maintainer sees why the listing is
        // stuck, and it does not sit in their queue.
        var mod = Mod(role: "maintainer", repoConnected: false, repoVerified: false, releases: 0);

        Assert.Equal("Waiting on the owner to connect a repository.", mod.NextStep);
        Assert.False(mod.NextStepIsYours);
        Assert.False(mod.NeedsAttention);
    }

    [Fact]
    public void A_maintainer_is_still_asked_to_do_their_own_job()
    {
        // Importing and fixing a failed import are a maintainer's to do, so these stay in their
        // queue - the point of being a maintainer is being able to act.
        var toImport = Mod(role: "maintainer", releases: 0);

        Assert.Equal("Tag a release and import it.", toImport.NextStep);
        Assert.True(toImport.NextStepIsYours);

        var broken = Mod(role: "maintainer", importError: "No .zip asset attached.");

        Assert.True(broken.NextStepIsYours);
    }

    [Fact]
    public void A_maintainer_cannot_publish_a_draft()
    {
        var mod = Mod(role: "maintainer", listingState: "unlisted");

        Assert.Equal("Still a draft. Only the owner can publish it.", mod.NextStep);
        Assert.False(mod.NextStepIsYours);
    }

    [Fact]
    public void Failed_validation_is_surfaced_once_everything_else_is_in_place()
    {
        var mod = Mod(failed: 2);

        Assert.Equal("2 release(s) failed validation.", mod.NextStep);
        Assert.True(mod.NeedsAttention);
    }
}
