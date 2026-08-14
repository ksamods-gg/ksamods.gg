using KsaMods.Exporter;
using KsaMods.Metadata;
using Xunit;

namespace KsaMods.Tests;

public class IndexBuilderTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static ExportListing Listing(
        string id = "AdvancedFlightComputer",
        string state = "listed",
        IReadOnlyDictionary<string, string>? links = null,
        string status = "active",
        string? supersededBy = null,
        IReadOnlyList<DependencyEntry>? dependencies = null,
        string type = ContentType.Mod,
        string? gameMin = "2026.8.3.5117",
        int? gameMinRevision = 5117,
        Metadata.ReleasesBlock? releases = null) => new()
        {
            Id = id,
            Type = type,
            Name = "Advanced Flight Computer",
            Authors = ["Maxi"],
            Abstract = "Extra maneuver planning tools.",
            License = "MIT",
            Tags = ["control"],
            Links = links ?? new Dictionary<string, string>
            {
                ["forums"] = "https://forums.ahwoo.com/threads/afc.783/",
            },
            Status = status,
            SupersededBy = supersededBy,
            GameMin = gameMin,
            GameMinRevision = gameMinRevision,
            Releases = releases,
            ListingState = state,
            Dependencies = dependencies ?? [],
        };

    private static ExportRelease Release(string modId = "AdvancedFlightComputer", string version = "1.0.0") => new()
    {
        ModId = modId,
        Version = version,
        Status = ReleaseStatus.Stable,
        ReleasedAt = Stamp,
        DownloadUrl = $"https://github.com/x/y/releases/download/v{version}/{modId}.zip",
        Sha256 = new string('a', 64),
        Size = 1024,
        GameMin = "2026.8.3.5117",
        GameMinRevision = 5117,
    };

    private static ExportInput Input(
        IReadOnlyList<ExportListing>? listings = null,
        IReadOnlyList<ExportRelease>? releases = null,
        IReadOnlyList<ExportModlistVersion>? modlists = null,
        IReadOnlyList<ExportTombstone>? tombstones = null) => new()
        {
            Listings = listings ?? [Listing()],
            Releases = releases ?? [Release()],
            Modlists = modlists ?? [],
            Tombstones = tombstones ?? [],
            GeneratedAt = Stamp,
        };

    [Fact]
    public void Exports_a_listing_and_its_releases()
    {
        var result = IndexBuilder.Build(Input());

        Assert.Contains(result.Files, f => f.Path == "listings/advancedflightcomputer.json");
        Assert.Contains(result.Files, f => f.Path == "releases/advancedflightcomputer/1.0.0.json");
        Assert.Contains(result.Files, f => f.Path == "index.json");
    }

    [Fact]
    public void Paths_are_lowercased_so_the_tree_behaves_the_same_on_every_platform()
    {
        // Git is case-insensitive on macOS and Windows and case-sensitive on Linux, so a tree
        // holding both MyMod.json and mymod.json behaves differently depending on who cloned it.
        var result = IndexBuilder.Build(Input(listings: [Listing(id: "MixedCaseMod")]));

        var listing = result.Files.Single(f => f.Path.StartsWith("listings/", StringComparison.Ordinal));
        Assert.Equal("listings/mixedcasemod.json", listing.Path);

        // The canonical casing survives inside the document.
        Assert.Contains("\"MixedCaseMod\"", listing.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_is_byte_identical_across_runs()
    {
        // The determinism requirement (§12.1): a run that changes nothing must produce no commit.
        var first = IndexBuilder.Build(Input());
        var second = IndexBuilder.Build(Input());

        Assert.Equal(first.Files.Count, second.Files.Count);
        for (var i = 0; i < first.Files.Count; i++)
        {
            Assert.Equal(first.Files[i].Path, second.Files[i].Path);
            Assert.Equal(first.Files[i].Content, second.Files[i].Content);
        }
    }

    [Fact]
    public void Output_does_not_depend_on_input_ordering()
    {
        var a = Listing(id: "Alpha");
        var b = Listing(id: "Beta");

        var forwards = IndexBuilder.Build(Input(listings: [a, b], releases: []));
        var backwards = IndexBuilder.Build(Input(listings: [b, a], releases: []));

        Assert.Equal(
            forwards.Files.Select(f => f.Path + f.Content),
            backwards.Files.Select(f => f.Path + f.Content));
    }

    [Fact]
    public void Uses_lf_endings_and_no_bom()
    {
        var content = IndexBuilder.Build(Input()).Files.First().Content;

        Assert.DoesNotContain("\r\n", content, StringComparison.Ordinal);
        Assert.False(content.StartsWith('﻿'));
    }

    [Fact]
    public void Skips_a_listing_with_no_forums_link()
    {
        // The site does not gate publishing on it, because app installation is stronger proof -
        // but RFC 0031 requires it, so the export does.
        var result = IndexBuilder.Build(Input(
            listings: [Listing(links: new Dictionary<string, string>())],
            releases: []));

        Assert.DoesNotContain(result.Files, f => f.Path.StartsWith("listings/", StringComparison.Ordinal));
        Assert.Contains(result.Skipped, s => s.Reason.Contains("forums", StringComparison.Ordinal));
    }

    [Fact]
    public void Skips_delisted_content_entirely()
    {
        // Exporting something the site has withdrawn would defeat the withdrawal: the index is
        // what other clients install from.
        var result = IndexBuilder.Build(Input(listings: [Listing(state: "delisted")], releases: []));

        Assert.DoesNotContain(result.Files, f => f.Path.StartsWith("listings/", StringComparison.Ordinal));
        Assert.Contains(result.Skipped, s => s.Reason.Contains("delisted", StringComparison.Ordinal));
    }

    [Fact]
    public void Skips_releases_of_a_listing_that_was_skipped()
    {
        // A release document referencing a listing nobody exported is a dangling record.
        var result = IndexBuilder.Build(Input(
            listings: [Listing(state: "unlisted")],
            releases: [Release()]));

        Assert.DoesNotContain(result.Files, f => f.Path.StartsWith("releases/", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_superseded_by_without_deprecation()
    {
        var result = IndexBuilder.Build(Input(
            listings: [Listing(status: "active", supersededBy: "Successor")],
            releases: []));

        Assert.Contains(result.Skipped, s => s.Reason.Contains("superseded_by", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_an_any_of_group_on_a_kind_that_cannot_offer_a_choice()
    {
        // any_of claims a choice, and a choice only exists where the loader will start without
        // that specific dependency.
        var result = IndexBuilder.Build(Input(
            listings:
            [
                Listing(dependencies:
                [
                    new DependencyEntry
                    {
                        Kind = DependencyKind.Conflict,
                        AnyOf = [new DependencyAlternative { Id = "A" }],
                    },
                ]),
            ],
            releases: []));

        Assert.Contains(result.Skipped, s => s.Reason.Contains("any_of", StringComparison.Ordinal));
    }

    [Fact]
    public void Exports_only_public_modlists()
    {
        var modlists = new[]
        {
            new ExportModlistVersion
            {
                ModlistId = "PublicPack", Name = "Public", Authors = ["Maxi"], Abstract = "x",
                License = "CC0-1.0", Version = "1.0.0", PublishedAt = Stamp, Visibility = "public",
                Mods = [new PinEntry { Id = "AdvancedFlightComputer", Version = "1.0.0" }],
            },
            new ExportModlistVersion
            {
                ModlistId = "PrivatePack", Name = "Private", Authors = ["Maxi"], Abstract = "x",
                License = "CC0-1.0", Version = "1.0.0", PublishedAt = Stamp, Visibility = "private",
            },
        };

        var result = IndexBuilder.Build(Input(modlists: modlists));

        Assert.Contains(result.Files, f => f.Path == "modlists/publicpack/1.0.0.json");
        Assert.DoesNotContain(result.Files, f => f.Path.Contains("privatepack", StringComparison.Ordinal));
    }

    [Fact]
    public void Exports_the_moderation_log_so_the_audit_trail_leaves_the_service()
    {
        var input = Input() with
        {
            Moderation =
            [
                new ExportModerationEntry
                {
                    Id = 1, Action = "delist", SubjectKind = "mod", SubjectId = "SomeMod",
                    Rationale = "reported as malware", CreatedAt = Stamp,
                },
            ],
        };

        var result = IndexBuilder.Build(input);

        var log = result.Files.Single(f => f.Path == "moderation-log.json");
        Assert.Contains("reported as malware", log.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Exports_modlist_aliases_as_a_redirect_map()
    {
        var input = Input() with
        {
            ModlistAliases = new Dictionary<string, string> { ["oldname"] = "NewName" },
        };

        var result = IndexBuilder.Build(input);

        var aliases = result.Files.Single(f => f.Path == "modlist-aliases.json");
        Assert.Contains("NewName", aliases.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_yanked_release_is_still_exported_and_marked()
    {
        // Clients need it: modlists pin it and dependency graphs reference it. It is simply not
        // offered for new installs.
        var result = IndexBuilder.Build(Input(
            releases: [Release() with { Yanked = true, YankedReason = "bad build" }]));

        var release = result.Files.Single(f => f.Path.StartsWith("releases/", StringComparison.Ordinal));
        Assert.Contains("\"yanked\": true", release.Content, StringComparison.Ordinal);
        Assert.Contains("bad build", release.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Stamps_the_resolved_revision_alongside_the_display_string()
    {
        // What lets a client evaluate compatibility offline, with no index lookup (RFC 0017).
        var release = IndexBuilder.Build(Input()).Files
            .Single(f => f.Path.StartsWith("releases/", StringComparison.Ordinal));

        Assert.Contains("\"game_min\": \"2026.8.3.5117\"", release.Content, StringComparison.Ordinal);
        Assert.Contains("\"game_min_revision\": 5117", release.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_release_carries_its_hash()
    {
        // A client that does not verify is non-conforming, and the export's job is to make
        // verifying the easy path.
        var release = IndexBuilder.Build(Input()).Files
            .Single(f => f.Path.StartsWith("releases/", StringComparison.Ordinal));

        Assert.Contains("\"sha256\"", release.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Stamps_spec_version_on_every_document()
    {
        foreach (var file in IndexBuilder.Build(Input()).Files
                     .Where(f => f.Path.StartsWith("listings/", StringComparison.Ordinal)
                              || f.Path.StartsWith("releases/", StringComparison.Ordinal)))
        {
            Assert.Contains("\"spec_version\": 1", file.Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_listing_without_game_min_is_skipped_with_a_reason()
    {
        // Required by RFC 0031, and not decoration: RFC 0017 reads a missing lower bound as
        // Unknown rather than "any", so a listing exported without one asks every client to
        // confirm every install by hand.
        var result = IndexBuilder.Build(Input([Listing(gameMin: null, gameMinRevision: null)]));

        Assert.DoesNotContain(result.Files, f => f.Path.StartsWith("listings/", StringComparison.Ordinal));
        Assert.Contains(result.Skipped, s => s.Reason.Contains("game_min", StringComparison.Ordinal));
    }

    [Fact]
    public void A_game_min_that_is_not_a_version_is_skipped()
    {
        var result = IndexBuilder.Build(Input([Listing(gameMin: "latest", gameMinRevision: null)]));

        Assert.Contains(result.Skipped, s => s.Reason.Contains("game_min", StringComparison.Ordinal));
    }

    [Fact]
    public void A_month_bound_exports_without_a_revision()
    {
        // A month that has not finished has no last revision yet (RFC 0033), and an open bound is
        // a legitimate state rather than a broken document.
        var result = IndexBuilder.Build(Input([Listing(gameMin: "2026.8", gameMinRevision: null)]));
        var listing = result.Files.Single(f => f.Path.StartsWith("listings/", StringComparison.Ordinal));

        Assert.Empty(result.Skipped);
        Assert.Contains("\"game_min\": \"2026.8\"", listing.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_release_takes_its_type_from_its_listing()
    {
        // It used to be hardcoded to "mod", which typed every StarMap release as a mod and told
        // clients the loader was something that needed one.
        var result = IndexBuilder.Build(Input(
            [Listing(id: "StarMap", type: ContentType.ModLoader)],
            [Release(modId: "StarMap")]));

        var release = result.Files.Single(f => f.Path.StartsWith("releases/", StringComparison.Ordinal));

        Assert.Contains("\"type\": \"mod-loader\"", release.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_release_inherits_the_listing_bound_when_it_has_none()
    {
        var result = IndexBuilder.Build(Input(
            [Listing(gameMin: "2026.7.1.4000", gameMinRevision: 4000)],
            [Release() with { GameMin = null, GameMinRevision = null }]));

        var release = result.Files.Single(f => f.Path.StartsWith("releases/", StringComparison.Ordinal));

        Assert.Contains("\"game_min\": \"2026.7.1.4000\"", release.Content, StringComparison.Ordinal);
        Assert.Contains("\"game_min_revision\": 4000", release.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_release_bound_wins_over_the_listing_bound()
    {
        // An amendment narrows one release. Inheriting over the top of it would undo the
        // amendment on the next export, which is the one thing an amendment must survive.
        var result = IndexBuilder.Build(Input(
            [Listing(gameMin: "2026.7.1.4000", gameMinRevision: 4000)],
            [Release()]));

        var release = result.Files.Single(f => f.Path.StartsWith("releases/", StringComparison.Ordinal));

        Assert.Contains("\"game_min_revision\": 5117", release.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_verified_repository_is_published_as_the_releases_block()
    {
        // What tells a consumer where new releases appear, and what RFC 0033 binds ownership to.
        var result = IndexBuilder.Build(Input(
            [Listing(releases: new Metadata.ReleasesBlock { GitHub = "Maxi/KSA-AFC" })]));

        var listing = result.Files.Single(f => f.Path.StartsWith("listings/", StringComparison.Ordinal));

        Assert.Contains("\"github\": \"Maxi/KSA-AFC\"", listing.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_release_carries_the_listing_as_it_read_at_stamp_time()
    {
        var snapshot = new ListingSnapshot
        {
            Name = "Advanced Flight Computer",
            Authors = ["Maxi"],
            Abstract = "What it said back then.",
            License = "MIT",
        };

        var result = IndexBuilder.Build(Input(releases: [Release() with { Listing = snapshot }]));
        var release = result.Files.Single(f => f.Path.StartsWith("releases/", StringComparison.Ordinal));

        Assert.Contains("\"listing\"", release.Content, StringComparison.Ordinal);
        Assert.Contains("What it said back then.", release.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void The_snapshot_carries_the_whole_index_not_a_count_of_it()
    {
        // RFC 0033's client contract: one fetch, and everything downstream - search, resolution,
        // compatibility - happens locally. It used to be three numbers and a timestamp, which told
        // a client how much it was about to fetch and nothing it could act on.
        var index = IndexBuilder.Build(Input()).Files.Single(f => f.Path == "index.json");

        Assert.Contains("\"snapshot_version\": 1", index.Content, StringComparison.Ordinal);
        Assert.Contains("Advanced Flight Computer", index.Content, StringComparison.Ordinal);
        Assert.Contains("\"sha256\"", index.Content, StringComparison.Ordinal);
        Assert.Contains("\"builds\"", index.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_withdrawn_listing_is_a_tombstone_and_nothing_more()
    {
        var result = IndexBuilder.Build(Input(
            tombstones: [new ExportTombstone { Id = "AbandonedThing", Status = "delisted" }]));

        var index = result.Files.Single(f => f.Path == "index.json");

        // Present, so a client can tell "removed" from "never listed" and stop offering an install
        // it has no other way to learn is gone.
        Assert.Contains("AbandonedThing", index.Content, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"delisted\"", index.Content, StringComparison.Ordinal);

        // And nothing of it anywhere else: a withdrawal that reaches a public mirror whole is a
        // withdrawal that did not happen.
        Assert.DoesNotContain(result.Files, f =>
            f.Path.StartsWith("listings/", StringComparison.Ordinal)
            && f.Path.Contains("abandonedthing", StringComparison.Ordinal));
    }

    [Fact]
    public void The_snapshot_omits_a_listing_that_failed_conformance()
    {
        // The per-file half already skips it. The snapshot is built from the same filtered set
        // rather than from the input, so the two halves cannot disagree about what is listed.
        var result = IndexBuilder.Build(Input(
            [Listing(), Listing(id: "NoBound", gameMin: null, gameMinRevision: null)]));

        var index = result.Files.Single(f => f.Path == "index.json");

        Assert.DoesNotContain("NoBound", index.Content, StringComparison.Ordinal);
        Assert.Contains("\"listings\": 1", index.Content, StringComparison.Ordinal);
    }
}

public class GitMirrorTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static ExportResult Export(params (string Path, string Content)[] files) => new()
    {
        Files = [.. files.Select(f => new ExportFile(f.Path, f.Content))],
        Skipped = [],
    };

    private static GitMirror Mirror(string path) => new(new GitMirrorOptions
    {
        WorkingDirectory = path,
        Push = false,
    });

    [Fact]
    public async Task Writes_files_and_commits_them()
    {
        var repository = CreateRepository();
        try
        {
            var outcome = await Mirror(repository).SyncAsync(
                Export(("listings/alpha.json", "{}\n")), "export", CancellationToken.None);

            Assert.True(outcome.Changed);
            Assert.NotNull(outcome.CommitSha);
            Assert.True(File.Exists(Path.Combine(repository, "listings", "alpha.json")));
        }
        finally { Cleanup(repository); }
    }

    [Fact]
    public async Task A_run_that_changes_nothing_produces_no_commit()
    {
        // Otherwise the history is noise and diffing two days apart tells you nothing (§12.1).
        var repository = CreateRepository();
        try
        {
            var export = Export(("listings/alpha.json", "{}\n"));
            var mirror = Mirror(repository);

            var first = await mirror.SyncAsync(export, "export", CancellationToken.None);
            var second = await mirror.SyncAsync(export, "export", CancellationToken.None);

            Assert.True(first.Changed);
            Assert.False(second.Changed);
            Assert.Null(second.CommitSha);
        }
        finally { Cleanup(repository); }
    }

    [Fact]
    public async Task Deletes_files_no_longer_in_the_export()
    {
        var repository = CreateRepository();
        try
        {
            var mirror = Mirror(repository);
            await mirror.SyncAsync(
                Export(("listings/alpha.json", "{}\n"), ("listings/beta.json", "{}\n")),
                "first", CancellationToken.None);

            var outcome = await mirror.SyncAsync(
                Export(("listings/alpha.json", "{}\n")), "second", CancellationToken.None);

            Assert.True(outcome.Changed);
            Assert.Equal(1, outcome.FilesDeleted);
            Assert.False(File.Exists(Path.Combine(repository, "listings", "beta.json")));
        }
        finally { Cleanup(repository); }
    }

    [Fact]
    public async Task Refuses_an_export_path_that_escapes_the_working_tree()
    {
        var repository = CreateRepository();
        try
        {
            await Assert.ThrowsAsync<GitMirrorException>(() => Mirror(repository).SyncAsync(
                Export(("../escaped.json", "{}\n")), "export", CancellationToken.None));
        }
        finally { Cleanup(repository); }
    }

    [Fact]
    public async Task Refuses_a_directory_that_is_not_a_git_working_tree()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ksamods-notgit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            await Assert.ThrowsAsync<GitMirrorException>(() => Mirror(path).SyncAsync(
                Export(("a.json", "{}\n")), "export", CancellationToken.None));
        }
        finally { Cleanup(path); }
    }

    private static string CreateRepository()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ksamods-mirror-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        Git(path, "init", "--initial-branch=main");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "user.name", "test");
        Git(path, "config", "commit.gpgsign", "false");

        return path;
    }

    private static void Git(string workingDirectory, params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(info)!;
        process.WaitForExit();
    }

    private static void Cleanup(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { /* best effort on Windows */ }
        catch (UnauthorizedAccessException) { }
    }
}
