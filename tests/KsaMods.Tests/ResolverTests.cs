using KsaMods.Metadata;
using KsaMods.Resolver;
using Xunit;

namespace KsaMods.Tests;

public class VersionBoundTests
{
    [Fact]
    public void Intersecting_takes_the_tighter_end_of_each_side()
    {
        var a = new VersionBound(SemVer.Parse("1.0.0"), SemVer.Parse("3.0.0"));
        var b = new VersionBound(SemVer.Parse("2.0.0"), SemVer.Parse("4.0.0"));

        var merged = a.Intersect(b);

        Assert.Equal(SemVer.Parse("2.0.0"), merged.Min);
        Assert.Equal(SemVer.Parse("3.0.0"), merged.Max);
    }

    [Fact]
    public void An_open_end_never_tightens_the_other_side()
    {
        var bounded = new VersionBound(SemVer.Parse("1.0.0"), SemVer.Parse("2.0.0"));
        var merged = bounded.Intersect(VersionBound.Any);

        Assert.Equal(SemVer.Parse("1.0.0"), merged.Min);
        Assert.Equal(SemVer.Parse("2.0.0"), merged.Max);
    }

    [Fact]
    public void Detects_an_impossible_intersection()
    {
        var a = new VersionBound(SemVer.Parse("3.0.0"), null);
        var b = new VersionBound(null, SemVer.Parse("2.0.0"));

        Assert.True(a.Intersect(b).IsEmpty);
    }

    [Fact]
    public void Bounds_are_inclusive_at_both_ends()
    {
        var bound = new VersionBound(SemVer.Parse("1.0.0"), SemVer.Parse("2.0.0"));

        Assert.True(bound.Allows(SemVer.Parse("1.0.0")));
        Assert.True(bound.Allows(SemVer.Parse("2.0.0")));
        Assert.False(bound.Allows(SemVer.Parse("2.0.1")));
    }
}

public class InstallPlannerTests
{
    private static CatalogueRelease Release(
        string id,
        string version,
        int? gameMin = null,
        int? gameMax = null,
        IReadOnlyList<ResolvedDependency>? dependencies = null,
        LoaderRequirement? loader = null,
        IReadOnlyList<string>? assetIds = null,
        bool yanked = false,
        bool overridesCore = false) => new()
        {
            ModId = id,
            Version = SemVer.Parse(version),
            GameMinRevision = gameMin,
            GameMaxRevision = gameMax,
            Dependencies = dependencies ?? [],
            Loader = loader,
            AssetIds = assetIds ?? [],
            Yanked = yanked,
            OverridesCore = overridesCore,
        };

    private static ResolvedDependency Dep(string id, string kind = DependencyKind.Required,
        string? min = null, string? max = null) => new()
        {
            ModId = id,
            Kind = kind,
            Bound = new VersionBound(
                min is null ? null : SemVer.Parse(min),
                max is null ? null : SemVer.Parse(max)),
        };

    private static ResolvePlan Plan(ICatalogue catalogue, int? gameRevision, params string[] wanted) =>
        new InstallPlanner(catalogue).Resolve(new ResolveRequest
        {
            Targets = [.. wanted.Select(w => new ResolveTarget(w))],
            GameRevision = gameRevision,
        });

    [Fact]
    public void Picks_the_newest_acceptable_version()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0"))
            .Add(Release("Alpha", "1.2.0"))
            .Add(Release("Alpha", "1.1.0"));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.True(plan.Satisfiable);
        Assert.Equal(SemVer.Parse("1.2.0"), plan.Order.Single().Version);
    }

    [Fact]
    public void Never_selects_a_yanked_release()
    {
        // A yank is not offered for new installs; an already-installed copy is left alone.
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0"))
            .Add(Release("Alpha", "1.1.0", yanked: true));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.Equal(SemVer.Parse("1.0.0"), plan.Order.Single().Version);
    }

    [Fact]
    public void Pulls_in_required_dependencies_transitively()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Beta")]))
            .Add(Release("Beta", "2.0.0", dependencies: [Dep("Gamma")]))
            .Add(Release("Gamma", "3.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.True(plan.Satisfiable);
        Assert.Equal(3, plan.Order.Count);
        Assert.Contains(plan.Order, p => p.ModId == "Gamma");
    }

    [Fact]
    public void Orders_dependencies_before_dependents()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Beta")]))
            .Add(Release("Beta", "2.0.0", dependencies: [Dep("Gamma")]))
            .Add(Release("Gamma", "3.0.0"));

        var order = Plan(catalogue, 5117, "Alpha").Order.Select(p => p.ModId).ToList();

        Assert.True(order.IndexOf("Gamma") < order.IndexOf("Beta"));
        Assert.True(order.IndexOf("Beta") < order.IndexOf("Alpha"));
    }

    [Fact]
    public void Hoists_core_overriding_mods_to_the_front()
    {
        // Asset ids are first-wins, so an override only works if it loads before Core - which
        // means being above it in manifest.toml.
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Plain", "1.0.0"))
            .Add(Release("Override", "1.0.0", overridesCore: true));

        var plan = Plan(catalogue, 5117, "Plain", "Override");

        Assert.Equal("Override", plan.Order[0].ModId);
        Assert.True(plan.Order[0].OverridesCore);
    }

    [Fact]
    public void Installs_the_loader_a_code_mod_needs()
    {
        // The loader is not repeated under dependencies - it has its own section - but it still
        // has to end up in the plan.
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0",
                loader: new LoaderRequirement("StarMap", new VersionBound(SemVer.Parse("0.4.5"), null))))
            .Add(Release("StarMap", "0.4.6"))
            .Add(Release("StarMap", "0.4.0"));

        var plan = Plan(catalogue, 5117, "Alpha");

        var order = plan.Order.Select(p => p.ModId).ToList();

        Assert.Equal(SemVer.Parse("0.4.6"), plan.Order.Single(p => p.ModId == "StarMap").Version);
        Assert.True(order.IndexOf("StarMap") < order.IndexOf("Alpha"));
    }

    [Fact]
    public void Honours_version_bounds_from_a_dependent()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Beta", max: "2.0.0")]))
            .Add(Release("Beta", "2.0.0"))
            .Add(Release("Beta", "3.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.Equal(SemVer.Parse("2.0.0"), plan.Order.Single(p => p.ModId == "Beta").Version);
    }

    [Fact]
    public void Intersects_bounds_from_several_dependents()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Shared", min: "2.0.0")]))
            .Add(Release("Beta", "1.0.0", dependencies: [Dep("Shared", max: "2.5.0")]))
            .Add(Release("Shared", "1.0.0"))
            .Add(Release("Shared", "2.5.0"))
            .Add(Release("Shared", "3.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha", "Beta");

        Assert.Equal(SemVer.Parse("2.5.0"), plan.Order.Single(p => p.ModId == "Shared").Version);
    }

    [Fact]
    public void Reports_bounds_that_cannot_be_satisfied_together()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Shared", min: "3.0.0")]))
            .Add(Release("Beta", "1.0.0", dependencies: [Dep("Shared", max: "2.0.0")]))
            .Add(Release("Shared", "2.0.0"))
            .Add(Release("Shared", "3.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha", "Beta");

        Assert.False(plan.Satisfiable);
        Assert.Contains(plan.Problems, p =>
            p.Kind is ProblemKind.BoundsUnsatisfiable or ProblemKind.NoCompatibleVersion);
    }

    [Fact]
    public void Excludes_incompatible_releases_but_keeps_untested_ones()
    {
        // Only Incompatible blocks. A false "incompatible" takes a working mod away for no
        // reason; a false "compatible" is a mod that can be removed again.
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", gameMin: 9999))       // Incompatible
            .Add(Release("Alpha", "0.9.0", gameMin: 5000, gameMax: 5100)); // Untested at 5117

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.True(plan.Satisfiable);
        Assert.Equal(SemVer.Parse("0.9.0"), plan.Order.Single().Version);
        Assert.Equal(CompatibilityState.Untested, plan.Order.Single().Compatibility);
    }

    [Fact]
    public void Explains_why_nothing_is_compatible()
    {
        var catalogue = new InMemoryCatalogue().Add(Release("Alpha", "1.0.0", gameMin: 9999));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.False(plan.Satisfiable);
        var problem = plan.Problems.Single();
        Assert.Equal(ProblemKind.NoCompatibleVersion, problem.Kind);
        Assert.Contains("9999", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unlisted_mod_is_a_problem_but_not_a_hard_stop_for_the_rest()
    {
        var catalogue = new InMemoryCatalogue().Add(Release("Alpha", "1.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha", "NeverHeardOfIt");

        Assert.True(plan.Satisfiable);
        Assert.Contains(plan.Order, p => p.ModId == "Alpha");
        Assert.Contains(plan.Problems, p => p.Kind == ProblemKind.UnknownMod);
    }

    [Fact]
    public void Detects_a_declared_conflict()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Beta", DependencyKind.Conflict)]))
            .Add(Release("Beta", "1.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha", "Beta");

        Assert.False(plan.Satisfiable);
        Assert.Contains(plan.Problems, p => p.Kind == ProblemKind.Conflict);
    }

    [Fact]
    public void Recommends_are_selected_by_default_and_deselectable()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Nice", DependencyKind.Recommends)]))
            .Add(Release("Nice", "1.0.0"));

        var included = new InstallPlanner(catalogue).Resolve(new ResolveRequest
        {
            Targets = [new ResolveTarget("Alpha")],
            GameRevision = 5117,
            IncludeRecommended = true,
        });
        Assert.Contains(included.Order, p => p.ModId == "Nice");

        var excluded = new InstallPlanner(catalogue).Resolve(new ResolveRequest
        {
            Targets = [new ResolveTarget("Alpha")],
            GameRevision = 5117,
            IncludeRecommended = false,
        });
        Assert.DoesNotContain(excluded.Order, p => p.ModId == "Nice");
        Assert.Contains("Nice", excluded.Suggested);
    }

    [Fact]
    public void Suggests_are_listed_but_never_installed()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Maybe", DependencyKind.Suggests)]))
            .Add(Release("Maybe", "1.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.DoesNotContain(plan.Order, p => p.ModId == "Maybe");
        Assert.Contains("Maybe", plan.Suggested);
    }

    [Fact]
    public void Optional_dependencies_are_not_installed_by_default()
    {
        // The authored mirror of StarMap's Optional = true: used when present, not pulled in.
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Extra", DependencyKind.Optional)]))
            .Add(Release("Extra", "1.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.DoesNotContain(plan.Order, p => p.ModId == "Extra");
    }

    [Fact]
    public void An_any_of_group_is_satisfied_by_one_member()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies:
            [
                new ResolvedDependency
                {
                    Kind = DependencyKind.Required,
                    Alternatives =
                    [
                        ("OpenALRouter", new VersionBound(SemVer.Parse("2.0.0"), null)),
                        ("ClassicALRouter", new VersionBound(SemVer.Parse("1.1.0"), null)),
                    ],
                },
            ]))
            .Add(Release("ClassicALRouter", "1.1.0"));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.True(plan.Satisfiable);
        Assert.Contains(plan.Order, p => p.ModId == "ClassicALRouter");
        Assert.DoesNotContain(plan.Order, p => p.ModId == "OpenALRouter");
    }

    [Fact]
    public void An_any_of_group_prefers_an_already_selected_member()
    {
        // Otherwise a plan can end up installing two implementations of the same thing.
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies:
            [
                new ResolvedDependency
                {
                    Kind = DependencyKind.Required,
                    Alternatives = [("RouterA", VersionBound.Any), ("RouterB", VersionBound.Any)],
                },
            ]))
            .Add(Release("RouterA", "1.0.0"))
            .Add(Release("RouterB", "1.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha", "RouterB");

        Assert.Contains(plan.Order, p => p.ModId == "RouterB");
        Assert.DoesNotContain(plan.Order, p => p.ModId == "RouterA");
    }

    [Fact]
    public void Returns_asset_id_collisions_within_the_resolved_set()
    {
        // The thing no generic mod host can do: KSA drops the duplicate with TryAdd, silently.
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", assetIds: ["FuelTank", "Alpha_Engine"]))
            .Add(Release("Beta", "1.0.0", assetIds: ["FuelTank"]));

        var plan = Plan(catalogue, 5117, "Alpha", "Beta");

        var collision = plan.Collisions.Single();
        Assert.Equal("FuelTank", collision.AssetId);
        Assert.Equal(["Alpha", "Beta"], collision.ModIds);
    }

    [Fact]
    public void A_collision_does_not_make_the_plan_unsatisfiable()
    {
        // It is a warning to surface before writing, not a refusal: the install works, one mod's
        // asset just loses.
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", assetIds: ["Shared"]))
            .Add(Release("Beta", "1.0.0", assetIds: ["Shared"]));

        var plan = Plan(catalogue, 5117, "Alpha", "Beta");

        Assert.True(plan.Satisfiable);
        Assert.Single(plan.Collisions);
    }

    [Fact]
    public void Honours_an_exact_pin()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0"))
            .Add(Release("Alpha", "2.0.0"));

        var plan = new InstallPlanner(catalogue).Resolve(new ResolveRequest
        {
            Targets = [new ResolveTarget("Alpha", SemVer.Parse("1.0.0"))],
            GameRevision = 5117,
        });

        Assert.Equal(SemVer.Parse("1.0.0"), plan.Order.Single().Version);
    }

    [Fact]
    public void Reports_a_pinned_version_that_does_not_exist()
    {
        var catalogue = new InMemoryCatalogue().Add(Release("Alpha", "1.0.0"));

        var plan = new InstallPlanner(catalogue).Resolve(new ResolveRequest
        {
            Targets = [new ResolveTarget("Alpha", SemVer.Parse("9.9.9"))],
            GameRevision = 5117,
        });

        Assert.False(plan.Satisfiable);
        Assert.Contains(plan.Problems, p => p.Kind == ProblemKind.PinnedVersionMissing);
    }

    [Fact]
    public void Backtracks_to_an_older_version_when_the_newest_cannot_work()
    {
        // Alpha 2.0.0 needs Beta ≥3, which does not exist; 1.0.0 needs Beta ≥1, which does.
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "2.0.0", dependencies: [Dep("Beta", min: "3.0.0")]))
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Beta", min: "1.0.0")]))
            .Add(Release("Beta", "1.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.True(plan.Satisfiable);
        Assert.Equal(SemVer.Parse("1.0.0"), plan.Order.Single(p => p.ModId == "Alpha").Version);
    }

    [Fact]
    public void A_dependency_cycle_terminates_instead_of_hanging()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Beta")]))
            .Add(Release("Beta", "1.0.0", dependencies: [Dep("Alpha")]));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.True(plan.Satisfiable);
        Assert.Equal(2, plan.Order.Count);
    }

    [Fact]
    public void Records_why_each_mod_is_in_the_plan()
    {
        var catalogue = new InMemoryCatalogue()
            .Add(Release("Alpha", "1.0.0", dependencies: [Dep("Beta")]))
            .Add(Release("Beta", "1.0.0"));

        var plan = Plan(catalogue, 5117, "Alpha");

        Assert.Equal("requested", plan.Order.Single(p => p.ModId == "Alpha").Reason);
        Assert.Contains("Alpha", plan.Order.Single(p => p.ModId == "Beta").Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_game_revision_yields_unknown_rather_than_blocking()
    {
        var catalogue = new InMemoryCatalogue().Add(Release("Alpha", "1.0.0", gameMin: 5000));

        var plan = Plan(catalogue, null, "Alpha");

        Assert.True(plan.Satisfiable);
        Assert.Equal(CompatibilityState.Unknown, plan.Order.Single().Compatibility);
    }
}
