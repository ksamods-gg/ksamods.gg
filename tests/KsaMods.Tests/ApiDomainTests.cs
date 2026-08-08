using KsaMods.Api.Domain;
using KsaMods.Metadata;
using Xunit;

namespace KsaMods.Tests;

public class PermissionTests
{
    private static Principal ModOwner => new() { AccountId = 1, ModRole = ModRole.Owner };
    private static Principal ModMaintainer => new() { AccountId = 2, ModRole = ModRole.Maintainer };
    private static Principal ListOwner => new() { AccountId = 3, ModlistRole = ModlistRole.Owner };
    private static Principal ListAdmin => new() { AccountId = 4, ModlistRole = ModlistRole.Admin };
    private static Principal ListEditor => new() { AccountId = 5, ModlistRole = ModlistRole.Editor };
    private static Principal Moderator => new() { AccountId = 6, SiteRole = SiteRole.Moderator };
    private static Principal Nobody => new() { AccountId = 7 };

    [Fact]
    public void An_editor_may_edit_a_draft_but_not_publish()
    {
        // Publishing mints an immutable version other people will install. Splitting it from
        // editing is the main reason `admin` exists as a distinct role.
        Assert.True(Permissions.Allows(ListEditor, Capability.EditModlistDraft));
        Assert.False(Permissions.Allows(ListEditor, Capability.PublishModlistVersion));

        Assert.True(Permissions.Allows(ListAdmin, Capability.PublishModlistVersion));
        Assert.True(Permissions.Allows(ListOwner, Capability.PublishModlistVersion));
    }

    [Fact]
    public void Only_the_mod_owner_may_connect_the_repository()
    {
        // The repository link is the ownership proof. A maintainer who could re-point it could
        // quietly take over the listing.
        Assert.True(Permissions.Allows(ModOwner, Capability.ConnectRepository));
        Assert.False(Permissions.Allows(ModMaintainer, Capability.ConnectRepository));
        Assert.False(Permissions.Allows(Moderator, Capability.ConnectRepository));
    }

    [Fact]
    public void A_maintainer_may_import_and_yank_but_not_manage_maintainers()
    {
        Assert.True(Permissions.Allows(ModMaintainer, Capability.ImportRelease));
        Assert.True(Permissions.Allows(ModMaintainer, Capability.YankRelease));
        Assert.False(Permissions.Allows(ModMaintainer, Capability.ManageMaintainers));
        Assert.False(Permissions.Allows(ModMaintainer, Capability.TransferModOwnership));
    }

    [Fact]
    public void Only_the_list_owner_may_delete_a_modlist()
    {
        Assert.True(Permissions.Allows(ListOwner, Capability.DeleteModlist));
        Assert.False(Permissions.Allows(ListAdmin, Capability.DeleteModlist));
        Assert.False(Permissions.Allows(Moderator, Capability.DeleteModlist));
    }

    [Fact]
    public void A_stranger_can_do_nothing()
    {
        foreach (var capability in Enum.GetValues<Capability>())
        {
            Assert.False(Permissions.Allows(Nobody, capability), $"{capability} leaked to a stranger");
        }
    }

    [Fact]
    public void A_moderator_cannot_publish_somebody_elses_modlist_version()
    {
        // Moderation is a withdrawal power, not an authoring one. A moderator publishing a
        // version under someone else's name is not a moderation action.
        Assert.False(Permissions.Allows(Moderator, Capability.PublishModlistVersion));
        Assert.False(Permissions.Allows(Moderator, Capability.EditModlistDraft));
    }

    [Fact]
    public void A_private_modlist_is_visible_only_to_its_collaborators_and_staff()
    {
        Assert.True(Permissions.CanViewModlist(ListEditor, "private", "listed"));
        Assert.True(Permissions.CanViewModlist(Moderator, "private", "listed"));
        Assert.False(Permissions.CanViewModlist(Nobody, "private", "listed"));
        Assert.False(Permissions.CanViewModlist(null, "private", "listed"));
    }

    [Fact]
    public void An_unlisted_modlist_is_link_reachable()
    {
        Assert.True(Permissions.CanViewModlist(null, "unlisted", "listed"));
    }

    [Fact]
    public void A_failed_release_stays_visible_to_its_maintainers_only()
    {
        // An author fixing a packaging mistake should never have to delete and recreate anything.
        Assert.True(Permissions.CanViewRelease(ModMaintainer, "failed", "listed"));
        Assert.True(Permissions.CanViewRelease(Moderator, "failed", "listed"));
        Assert.False(Permissions.CanViewRelease(Nobody, "failed", "listed"));
        Assert.False(Permissions.CanViewRelease(null, "pending", "listed"));
    }

    [Fact]
    public void A_passed_release_of_a_listed_mod_is_public()
    {
        Assert.True(Permissions.CanViewRelease(null, "passed", "listed"));
        Assert.True(Permissions.CanViewRelease(null, "passed_warnings", "listed"));
    }
}

public class ReleaseAmendmentTests
{
    private static ReleaseFacts Facts(
        int? gameMin = 5000, int? gameMax = null,
        string? loaderMin = null, string? loaderMax = null,
        IReadOnlyList<DependencyFact>? dependencies = null,
        bool yanked = false) => new()
        {
            GameMinRevision = gameMin,
            GameMaxRevision = gameMax,
            LoaderMin = loaderMin,
            LoaderMax = loaderMax,
            Dependencies = dependencies ?? [],
            Yanked = yanked,
        };

    [Fact]
    public void Raising_the_game_minimum_narrows_and_is_allowed()
    {
        Assert.Empty(ReleaseAmendment.Check(Facts(gameMin: 5000), Facts(gameMin: 5100)));
    }

    [Fact]
    public void Lowering_the_game_minimum_widens_and_is_rejected()
    {
        // Claims support for builds nobody re-verified against the actual archive.
        var rejections = ReleaseAmendment.Check(Facts(gameMin: 5100), Facts(gameMin: 5000));
        Assert.Contains(rejections, r => r.Field == "game_min_revision");
    }

    [Fact]
    public void Adding_a_game_maximum_narrows_and_is_allowed()
    {
        // The everyday case: a mod turns out to break above a certain build, so it gets its
        // compatibility tightened rather than yanked and stays installable where it works.
        Assert.Empty(ReleaseAmendment.Check(Facts(gameMax: null), Facts(gameMax: 5200)));
    }

    [Theory]
    [InlineData(5200, 5300)]   // raising a ceiling widens
    [InlineData(5200, null)]   // removing a ceiling widens
    public void Widening_the_game_maximum_is_rejected(int current, int? proposed)
    {
        var rejections = ReleaseAmendment.Check(Facts(gameMax: current), Facts(gameMax: proposed));
        Assert.Contains(rejections, r => r.Field == "game_max_revision");
    }

    [Fact]
    public void A_dependency_may_be_added_but_never_removed()
    {
        var current = Facts(dependencies: [new DependencyFact { DepId = "Beta", Kind = "required" }]);
        var removed = Facts(dependencies: []);

        Assert.Contains(ReleaseAmendment.Check(current, removed), r => r.Field.Contains("Beta", StringComparison.Ordinal));

        var added = Facts(dependencies:
        [
            new DependencyFact { DepId = "Beta", Kind = "required" },
            new DependencyFact { DepId = "Gamma", Kind = "conflict" },
        ]);

        Assert.Empty(ReleaseAmendment.Check(current, added));
    }

    [Fact]
    public void A_dependency_bound_may_only_tighten()
    {
        var current = Facts(dependencies: [new DependencyFact { DepId = "Beta", Kind = "required", Min = "1.0.0" }]);

        var tightened = Facts(dependencies: [new DependencyFact { DepId = "Beta", Kind = "required", Min = "1.5.0" }]);
        Assert.Empty(ReleaseAmendment.Check(current, tightened));

        var loosened = Facts(dependencies: [new DependencyFact { DepId = "Beta", Kind = "required", Min = "0.5.0" }]);
        Assert.NotEmpty(ReleaseAmendment.Check(current, loosened));
    }

    [Fact]
    public void A_dependency_kind_cannot_change()
    {
        var current = Facts(dependencies: [new DependencyFact { DepId = "Beta", Kind = "required" }]);
        var changed = Facts(dependencies: [new DependencyFact { DepId = "Beta", Kind = "optional" }]);

        Assert.Contains(ReleaseAmendment.Check(current, changed), r => r.Field.EndsWith(".kind", StringComparison.Ordinal));
    }

    [Fact]
    public void A_yank_cannot_be_reversed()
    {
        // Reversing it silently would let a compromised build return without review.
        var rejections = ReleaseAmendment.Check(Facts(yanked: true), Facts(yanked: false));
        Assert.Contains(rejections, r => r.Field == "yanked");
    }

    [Fact]
    public void Yanking_is_always_permitted()
    {
        Assert.Empty(ReleaseAmendment.Check(Facts(yanked: false), Facts(yanked: true)));
    }

    [Fact]
    public void The_invariant_holds_across_every_field_at_once()
    {
        // The whole point, stated as one assertion: a release can never become more permissive.
        var current = Facts(gameMin: 5100, gameMax: 5200, loaderMin: "0.4.5", loaderMax: "0.5.0");
        var wider = Facts(gameMin: 5000, gameMax: 5300, loaderMin: "0.4.0", loaderMax: "0.6.0");

        var rejections = ReleaseAmendment.Check(current, wider);

        Assert.Equal(4, rejections.Count);
    }
}

public class ModlistPublishTests
{
    private static MemberRelease Member(
        string id, string version, int? gameMin = null, int? gameMax = null,
        bool yanked = false, string listingState = "listed", string status = "active",
        IReadOnlyList<string>? assetIds = null, IReadOnlyList<string>? requires = null) => new()
        {
            ModId = id,
            Version = version,
            GameMinRevision = gameMin,
            GameMaxRevision = gameMax,
            Yanked = yanked,
            ListingState = listingState,
            Status = status,
            AssetIds = assetIds ?? [],
            RequiredDependencies = requires ?? [],
        };

    private static Dictionary<string, IReadOnlyList<MemberRelease>> Catalogue(params MemberRelease[] releases) =>
        releases
            .GroupBy(r => r.ModId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MemberRelease>)[.. g], StringComparer.OrdinalIgnoreCase);

    private static DraftEntry Entry(string id, string? version = null, int position = 0) =>
        new("mod", id, version, position, null);

    [Fact]
    public void An_empty_list_cannot_be_published()
    {
        var result = ModlistPublish.Prepare([], Catalogue(), confirmed: false);

        Assert.False(result.CanPublish);
        Assert.Contains(result.Issues, i => i.Kind == PublishIssueKind.EmptyList && i.Blocking);
    }

    [Fact]
    public void An_unpinned_entry_resolves_to_an_exact_version()
    {
        // A published modlist never contains a floating reference.
        var result = ModlistPublish.Prepare(
            [Entry("Alpha")],
            Catalogue(Member("Alpha", "1.0.0"), Member("Alpha", "1.2.0")),
            confirmed: false);

        Assert.True(result.CanPublish);
        Assert.Equal("1.2.0", result.Mods.Single().Version);
    }

    [Fact]
    public void A_pinned_version_that_does_not_exist_blocks()
    {
        var result = ModlistPublish.Prepare(
            [Entry("Alpha", "9.9.9")],
            Catalogue(Member("Alpha", "1.0.0")),
            confirmed: false);

        Assert.False(result.CanPublish);
        Assert.Contains(result.Issues, i => i.Kind == PublishIssueKind.PinnedVersionMissing && i.Blocking);
    }

    [Fact]
    public void An_unknown_member_blocks()
    {
        var result = ModlistPublish.Prepare([Entry("NeverHeardOfIt")], Catalogue(), confirmed: false);

        Assert.False(result.CanPublish);
        Assert.Contains(result.Issues, i => i.Kind == PublishIssueKind.UnknownMember && i.Blocking);
    }

    [Fact]
    public void A_yanked_member_warns_and_is_overridable()
    {
        var draft = new[] { Entry("Alpha", "1.0.0") };
        var catalogue = Catalogue(Member("Alpha", "1.0.0", yanked: true));

        var unconfirmed = ModlistPublish.Prepare(draft, catalogue, confirmed: false);
        Assert.False(unconfirmed.CanPublish);
        Assert.Contains(unconfirmed.Issues, i => i.Kind == PublishIssueKind.MemberYanked && !i.Blocking);

        var confirmed = ModlistPublish.Prepare(draft, catalogue, confirmed: true);
        Assert.True(confirmed.CanPublish);
    }

    [Fact]
    public void Compatibility_is_the_intersection_of_every_member()
    {
        // A list works only where every member works.
        var result = ModlistPublish.Prepare(
            [Entry("Alpha", position: 0), Entry("Beta", position: 1)],
            Catalogue(
                Member("Alpha", "1.0.0", gameMin: 5000, gameMax: 5300),
                Member("Beta", "1.0.0", gameMin: 5100, gameMax: 5200)),
            confirmed: false);

        Assert.Equal(5100, result.GameMinRevision);
        Assert.Equal(5200, result.GameMaxRevision);
    }

    [Fact]
    public void An_empty_compatibility_intersection_warns_but_does_not_block()
    {
        // The members may still be individually installable, and only Incompatible blocks.
        var result = ModlistPublish.Prepare(
            [Entry("Alpha", position: 0), Entry("Beta", position: 1)],
            Catalogue(
                Member("Alpha", "1.0.0", gameMin: 5300),
                Member("Beta", "1.0.0", gameMax: 5100)),
            confirmed: true);

        Assert.True(result.CanPublish);
        Assert.Contains(result.Issues, i => i.Kind == PublishIssueKind.NoCommonCompatibility && !i.Blocking);
    }

    [Fact]
    public void Colliding_asset_ids_between_members_are_surfaced()
    {
        var result = ModlistPublish.Prepare(
            [Entry("Alpha", position: 0), Entry("Beta", position: 1)],
            Catalogue(
                Member("Alpha", "1.0.0", assetIds: ["FuelTank"]),
                Member("Beta", "1.0.0", assetIds: ["FuelTank"])),
            confirmed: false);

        var issue = result.Issues.Single(i => i.Kind == PublishIssueKind.AssetCollision);
        Assert.Contains("FuelTank", issue.Detail, StringComparison.Ordinal);
        Assert.False(issue.Blocking);
    }

    [Fact]
    public void A_missing_required_dependency_is_flagged()
    {
        // The check that earns curation its name: the game resolves nothing and StarMap fails to
        // a console nobody reads, so this ships a mod that never loads and never says why.
        var result = ModlistPublish.Prepare(
            [Entry("Alpha")],
            Catalogue(Member("Alpha", "1.0.0", requires: ["MissingLib"])),
            confirmed: false);

        Assert.Contains(result.Issues, i =>
            i.Kind == PublishIssueKind.MissingDependency &&
            i.Detail.Contains("MissingLib", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dependency_present_in_the_list_is_not_flagged()
    {
        var result = ModlistPublish.Prepare(
            [Entry("Alpha", position: 0), Entry("Beta", position: 1)],
            Catalogue(
                Member("Alpha", "1.0.0", requires: ["Beta"]),
                Member("Beta", "1.0.0")),
            confirmed: false);

        Assert.DoesNotContain(result.Issues, i => i.Kind == PublishIssueKind.MissingDependency);
    }

    [Fact]
    public void A_clean_list_publishes_without_confirmation()
    {
        var result = ModlistPublish.Prepare(
            [Entry("Alpha")],
            Catalogue(Member("Alpha", "1.0.0", gameMin: 5000)),
            confirmed: false);

        Assert.True(result.CanPublish);
        Assert.False(result.NeedsConfirmation);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void A_deprecated_or_delisted_member_warns()
    {
        var result = ModlistPublish.Prepare(
            [Entry("Alpha", position: 0), Entry("Beta", position: 1)],
            Catalogue(
                Member("Alpha", "1.0.0", status: "deprecated"),
                Member("Beta", "1.0.0", listingState: "delisted")),
            confirmed: true);

        Assert.Contains(result.Issues, i => i.Kind == PublishIssueKind.MemberDeprecated);
        Assert.Contains(result.Issues, i => i.Kind == PublishIssueKind.MemberDelisted);
        Assert.True(result.CanPublish);
    }

    [Fact]
    public void Pins_preserve_draft_order()
    {
        var result = ModlistPublish.Prepare(
            [Entry("Gamma", position: 0), Entry("Alpha", position: 1), Entry("Beta", position: 2)],
            Catalogue(Member("Gamma", "1.0.0"), Member("Alpha", "1.0.0"), Member("Beta", "1.0.0")),
            confirmed: false);

        Assert.Equal(["Gamma", "Alpha", "Beta"], result.Mods.Select(m => m.Id));
    }
}
