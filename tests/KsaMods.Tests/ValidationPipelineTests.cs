using KsaMods.Metadata;
using KsaMods.Tests.Fixtures;
using KsaMods.Validation;
using Xunit;

namespace KsaMods.Tests;

public class ValidationPipelineTests
{
    private static ValidationResult Run(MemoryStream archive, string expectedId, ArchiveLimits? limits = null) =>
        ValidationPipeline.Validate(new ValidationRequest
        {
            Archive = archive,
            ExpectedId = expectedId,
            Limits = limits ?? ArchiveLimits.Default,
        });

    private static bool Has(ValidationResult r, string code) => r.Findings.Any(f => f.Code == code);

    // ────────────────────── the well-formed shapes ──────────────────────

    [Fact]
    public void A_well_formed_code_mod_passes()
    {
        var result = Run(ModArchives.CodeMod(), ModArchives.CodeModId);

        Assert.Equal(ValidationOutcome.Passed, result.Outcome);
        Assert.Equal(ModArchives.CodeModId, result.Facts.InstallRoot);
        Assert.True(result.Facts.InstallRootDerived);
        Assert.True(result.Facts.ShipsCode);
        Assert.Equal(2, result.Facts.Assemblies.Count);
        Assert.Contains(result.Facts.Assemblies, a => a.IsEntry);
    }

    [Fact]
    public void A_well_formed_content_mod_passes_and_does_not_warn_about_its_textures()
    {
        // The regression that matters most in stage 5. Textures are referenced from inside the
        // declared XML, never named in mod.toml - the correct shape of every content mod. A
        // validator that warns here fires on everything and gets ignored.
        var result = Run(ModArchives.ContentMod(), ModArchives.ContentModId);

        Assert.Equal(ValidationOutcome.Passed, result.Outcome);
        Assert.False(Has(result, FindingCodes.UnreachableContentFile));
        Assert.Contains("OuterPlanets/Textures/Persephone_Diffuse.ktx2", result.Facts.ReachablePaths);
    }

    [Fact]
    public void Extracts_every_asset_id_for_the_collision_index()
    {
        var result = Run(ModArchives.ContentMod(), ModArchives.ContentModId);

        Assert.Contains(result.Facts.AssetIds, a => a.Id == "OuterPlanets_Persephone");
        Assert.Contains(result.Facts.AssetIds, a => a.Id == "OuterPlanets_PersephoneBody");
    }

    // ────────────────────── stage 3: archive safety ──────────────────────

    [Fact]
    public void Rejects_path_traversal()
    {
        var result = Run(ModArchives.PathTraversal(), ModArchives.ContentModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.PathTraversal));
    }

    [Fact]
    public void Rejects_symlink_entries()
    {
        var result = Run(ModArchives.ContainsSymlink(), ModArchives.ContentModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.NonRegularEntry));
    }

    [Fact]
    public void Rejects_nested_archives()
    {
        var result = Run(ModArchives.NestedArchive(), ModArchives.ContentModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.NestedArchive));
    }

    [Fact]
    public void Rejects_a_zip_bomb_without_expanding_it()
    {
        // Must fail on the declared sizes and the ratio, never by actually inflating 64 MB.
        var result = Run(ModArchives.ZipBomb(), ModArchives.ContentModId);

        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(
            Has(result, FindingCodes.UncompressedSizeExceeded) ||
            Has(result, FindingCodes.CompressionRatioExceeded));
    }

    [Fact]
    public void Rejects_an_archive_carrying_the_games_own_manifest()
    {
        // manifest.toml lives in the documents root and never inside a mod (spec §8).
        var result = Run(ModArchives.ContainsReservedFile(), ModArchives.ContentModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.ReservedFilePresent));
    }

    [Fact]
    public void Strips_archiver_junk_rather_than_rejecting_it()
    {
        var result = Run(ModArchives.WithJunkFiles(), ModArchives.ContentModId);

        Assert.NotEqual(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.JunkFileStripped));
        // Crucially, __MACOSX must not have counted as a second top-level directory.
        Assert.False(Has(result, FindingCodes.NotExactlyOneRoot));
    }

    // ────────────────────── stage 4: structure ──────────────────────

    [Fact]
    public void Rejects_a_root_directory_that_is_not_the_claimed_id()
    {
        var result = Run(ModArchives.WrongRootName(), ModArchives.ContentModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.RootNameMismatch));
    }

    [Fact]
    public void Rejects_more_than_one_top_level_directory()
    {
        var result = Run(ModArchives.TwoRoots(), ModArchives.ContentModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.NotExactlyOneRoot));
    }

    [Fact]
    public void Root_name_comparison_is_case_insensitive()
    {
        var result = Run(ModArchives.ContentMod(), "outerplanets");
        Assert.False(Has(result, FindingCodes.RootNameMismatch));
    }

    // ────────────────────── stage 5: declaration integrity ──────────────────────

    [Fact]
    public void A_declared_path_that_does_not_exist_is_an_error()
    {
        // The single most common way a content mod installs cleanly and does nothing.
        var result = Run(ModArchives.MissingDeclaredPath(), ModArchives.ContentModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.DeclaredPathMissing));
    }

    [Fact]
    public void Malformed_declared_xml_is_an_error()
    {
        var result = Run(ModArchives.MalformedXml(), ModArchives.ContentModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.DeclaredXmlUnparseable));
    }

    [Fact]
    public void The_wrong_xml_root_element_is_an_error()
    {
        var result = Run(ModArchives.WrongXmlRoot(), ModArchives.ContentModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.WrongXmlRoot));
    }

    [Fact]
    public void Content_reachable_from_nothing_warns_but_does_not_fail()
    {
        var result = Run(ModArchives.OrphanedContent(), ModArchives.ContentModId);

        Assert.Equal(ValidationOutcome.PassedWithWarnings, result.Outcome);
        Assert.True(Has(result, FindingCodes.UnreachableContentFile));
    }

    [Fact]
    public void An_xxe_payload_is_neutralised_rather_than_resolved()
    {
        // DtdProcessing.Prohibit means the document does not parse at all, which is the right
        // outcome: the entity is never expanded and no file is read.
        var result = Run(ModArchives.XxeAttempt(), ModArchives.ContentModId);

        Assert.True(Has(result, FindingCodes.DeclaredXmlUnparseable));
        Assert.DoesNotContain(result.Facts.AssetIds, a => a.Id.Contains("root:", StringComparison.Ordinal));
    }

    // ────────────────────── stage 7: code facet ──────────────────────

    [Fact]
    public void Shipping_a_game_assembly_is_an_error()
    {
        var result = Run(ModArchives.ShipsGameAssembly(), ModArchives.CodeModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.GameAssemblyShipped));
    }

    [Fact]
    public void Shipping_the_loader_api_warns_rather_than_rejects()
    {
        // AircraftHUD ships StarMap.API.dll, so rejecting would exclude a real published mod.
        var result = Run(ModArchives.ShipsLoaderAssembly(), ModArchives.CodeModId);

        Assert.Equal(ValidationOutcome.PassedWithWarnings, result.Outcome);
        Assert.True(Has(result, FindingCodes.LoaderAssemblyShipped));
    }

    [Fact]
    public void No_starmap_section_falls_back_to_the_mod_id_as_entry_assembly()
    {
        // Why AircraftHUD loads with no [StarMap] block at all: the loader defaults
        // EntryAssembly to the mod id.
        var result = Run(ModArchives.NoStarMapSectionButEntryDllPresent(), ModArchives.CodeModId);

        Assert.False(Has(result, FindingCodes.EntryAssemblyMissing));
        Assert.Contains(result.Facts.Assemblies, a => a.IsEntry);
    }

    [Fact]
    public void A_declared_entry_assembly_that_is_not_shipped_is_an_error()
    {
        var result = Run(ModArchives.EntryAssemblyMissing(), ModArchives.CodeModId);
        Assert.Equal(ValidationOutcome.Failed, result.Outcome);
        Assert.True(Has(result, FindingCodes.EntryAssemblyMissing));
    }

    [Fact]
    public void Reads_assembly_identity_without_loading_the_assembly()
    {
        var result = Run(ModArchives.CodeMod(), ModArchives.CodeModId);
        Assert.All(result.Facts.Assemblies, a => Assert.False(string.IsNullOrEmpty(a.AssemblyName)));
    }

    // ────────────────────── stage 7b and 8 ──────────────────────

    [Fact]
    public void Extracts_starmap_dependencies_with_their_optional_flag()
    {
        var result = Run(ModArchives.WithModDependencies(), ModArchives.CodeModId);

        Assert.Equal(2, result.Facts.Dependencies.Count);

        var optional = result.Facts.Dependencies.Single(d => d.ModId == "KittenExtensions");
        Assert.True(optional.Optional);
        Assert.Contains("KittenExtensions", optional.ImportedAssemblies);

        // Absent Optional means required: the loader refuses to start the mod without it.
        var required = result.Facts.Dependencies.Single(d => d.ModId == "CoreLib");
        Assert.False(required.Optional);
    }

    [Fact]
    public void Records_console_commands_verbatim()
    {
        var result = Run(ModArchives.WithConsoleBlock(), ModArchives.ContentModId);

        Assert.True(Has(result, FindingCodes.ConsoleBlockPresent));
        Assert.Equal(3, result.Facts.ConsoleCommands.Count);
        Assert.Contains(result.Facts.ConsoleCommands,
            c => c.Hook == "onLoad" && c.Command == "camera map 2.88 0.47 3635076");
    }

    [Fact]
    public void Flags_asset_ids_that_override_core()
    {
        var result = ValidationPipeline.Validate(new ValidationRequest
        {
            Archive = ModArchives.ContentMod(),
            ExpectedId = ModArchives.ContentModId,
            CoreAssetIds = new HashSet<string>(StringComparer.Ordinal) { "OuterPlanets_Persephone" },
        });

        Assert.True(Has(result, FindingCodes.CoreIdOverride));
    }

    // ────────────────────── behaviour of the pipeline itself ──────────────────────

    [Fact]
    public void Reports_every_problem_in_one_run_rather_than_one_per_push()
    {
        // Total, not fail-fast (§7.4): an author should not need five imports to learn five things.
        var archive = ArchiveBuilder.New()
            .WithFile($"{ModArchives.ContentModId}/mod.toml", """
                name = "x"
                assets = [ "Assets/Missing.xml", "Assets/AlsoMissing.xml" ]
                systems = [ "Systems/GoneToo.xml" ]
                """)
            .Build();

        var result = Run(archive, ModArchives.ContentModId);

        Assert.Equal(3, result.Findings.Count(f => f.Code == FindingCodes.DeclaredPathMissing));
    }

    [Fact]
    public void A_hostile_mod_toml_cannot_escape_the_archive_root()
    {
        // Declared paths are resolved inside the root, mirroring what the loader does when it
        // Path.Combines onto the mod folder - so ../.. resolves to nothing readable, not to a
        // host file.
        var archive = ArchiveBuilder.New()
            .WithFile($"{ModArchives.ContentModId}/mod.toml", """
                name = "x"
                assets = [ "../../../../etc/passwd" ]
                """)
            .Build();

        var result = Run(archive, ModArchives.ContentModId);
        Assert.True(Has(result, FindingCodes.DeclaredPathMissing));
    }
}
