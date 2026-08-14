using KsaMods.Metadata;
using Xunit;

namespace KsaMods.Tests;

public class InstallDescriptorTests
{
    [Fact]
    public void A_mod_needs_no_install_section_at_all()
    {
        // The convention is the default, now stated rather than assumed: the mods folder, as a
        // folder named by the id. Every file written against RFC 0031 stays valid.
        Assert.Null(InstallDescriptor.Check(ContentType.Mod, null, null));
        Assert.Null(InstallDescriptor.Check(ContentType.Mod, new InstallBlock { Root = "build/AFC" }, null));
    }

    [Fact]
    public void A_loader_has_no_default_and_must_say_where_it_goes()
    {
        // There is no convention for a loader, and a manager must not guess.
        Assert.NotNull(InstallDescriptor.Check(
            ContentType.ModLoader, new InstallBlock { Root = "StarMap" }, null));

        Assert.Null(InstallDescriptor.Check(
            ContentType.ModLoader,
            new InstallBlock { Target = InstallAnchor.Standalone },
            new ProvidesBlock { Launch = "StarMap.exe" }));
    }

    [Fact]
    public void Standalone_without_a_launch_is_a_directory_nothing_reaches()
    {
        Assert.NotNull(InstallDescriptor.Check(
            ContentType.ModLoader,
            new InstallBlock { Target = InstallAnchor.Standalone },
            new ProvidesBlock { ContentDir = InstallAnchor.Mods }));
    }

    [Fact]
    public void A_pack_installs_nothing_of_its_own()
    {
        Assert.NotNull(InstallDescriptor.Check(ContentType.ModPack, new InstallBlock(), null));
    }

    [Fact]
    public void Provides_belongs_only_to_a_loader()
    {
        Assert.NotNull(InstallDescriptor.Check(ContentType.Mod, null, new ProvidesBlock()));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("~/.ssh/config")]
    [InlineData("C:/Windows")]
    [InlineData("../../elsewhere")]
    [InlineData("a/../../elsewhere")]
    [InlineData(@"build\AFC")]
    public void A_path_that_leaves_its_anchor_makes_the_file_invalid(string path)
    {
        // Containment is a validity rule, not a recommendation: this descriptor is executed by a
        // manager holding write access to a game directory.
        Assert.NotNull(InstallDescriptor.Check(ContentType.Mod, new InstallBlock { Root = path }, null));
    }

    [Fact]
    public void Walking_back_inside_the_anchor_is_fine()
    {
        // a/../b stays put while a/../../b escapes, and only counting segments tells them apart.
        Assert.Null(InstallDescriptor.Check(
            ContentType.Mod, new InstallBlock { Root = "build/./x/../AFC" }, null));
    }

    [Fact]
    public void The_game_owns_its_own_manifest()
    {
        // ModManifest.Save rewrites it wholesale from the game's in-memory list.
        Assert.NotNull(InstallDescriptor.Check(
            ContentType.Save,
            new InstallBlock { Target = InstallAnchor.UserData, Manages = ["manifest.toml"] },
            null));
    }

    [Fact]
    public void An_unknown_anchor_or_format_is_rejected_rather_than_ignored()
    {
        // A future value means a layout this manager cannot perform. Guessing writes files
        // somewhere the author did not choose.
        Assert.NotNull(InstallDescriptor.Check(
            ContentType.Mod, new InstallBlock { Target = "somewhere-else" }, null));

        Assert.NotNull(InstallDescriptor.Check(
            ContentType.ModLoader,
            new InstallBlock { Target = InstallAnchor.GameRoot },
            new ProvidesBlock
            {
                Launch = "StarMap.exe",
                Configure = new ConfigureBlock { File = "c.yaml", Format = "yaml" },
            }));
    }

    [Fact]
    public void The_StarMap_descriptor_from_the_RFC_is_valid()
    {
        Assert.Null(InstallDescriptor.Check(
            ContentType.ModLoader,
            new InstallBlock
            {
                Target = InstallAnchor.Standalone,
                Uninstall = ["Delete the StarMap directory."],
            },
            new ProvidesBlock
            {
                Launch = "StarMap.exe",
                ContentDir = InstallAnchor.Mods,
                Configure = new ConfigureBlock
                {
                    File = "StarMapConfig.json",
                    Format = InstallDescriptor.FormatJson,
                    GamePath = "GameLocation",
                },
            }));
    }
}
