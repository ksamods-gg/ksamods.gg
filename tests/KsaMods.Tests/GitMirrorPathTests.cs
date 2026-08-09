using KsaMods.Exporter;
using Xunit;

namespace KsaMods.Tests;

/// <summary>
/// The mirror against a real working tree, with the working directory written the way a container
/// environment variable writes it.
///
/// <para>Both of these failed when first run for real, and neither was visible to a test that
/// compared <c>ExportResult</c> objects: one deleted every document it had just written because two
/// paths for the same file were compared as strings, and the other committed on every run because
/// the index carries a timestamp. Together they produced a mirror that was empty and busy.</para>
/// </summary>
public sealed class GitMirrorPathTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"ksamods-mirror-{Guid.NewGuid():N}");

    public GitMirrorPathTests()
    {
        Directory.CreateDirectory(_root);
        Run("init", "--quiet");
    }

    public void Dispose()
    {
        try
        {
            // git leaves read-only objects behind on Windows.
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is untidy, not a failure.
        }
    }

    [Fact]
    public async Task Documents_survive_a_working_directory_written_with_forward_slashes()
    {
        // The shape a container gives it: MIRROR_DIRECTORY=/mirror, or a Windows path typed with
        // forward slashes. EnumerateFiles echoes that prefix back, and the deletion pass compared
        // it against a normalised path - so nothing matched and everything went.
        var mirror = new GitMirror(new GitMirrorOptions
        {
            WorkingDirectory = _root.Replace('\\', '/'),
            Push = false,
        });

        var export = new ExportResult
        {
            Files =
            [
                new ExportFile("index.json", "{\"generated_at\":\"2026-01-01T00:00:00Z\"}"),
                new ExportFile("listings/example.json", "{\"id\":\"Example\"}"),
                new ExportFile("releases/example@1.0.0.json", "{\"version\":\"1.0.0\"}"),
            ],
            Skipped = [],
        };

        var outcome = await mirror.SyncAsync(export, "first", CancellationToken.None);

        Assert.True(outcome.Changed);
        Assert.Equal(0, outcome.FilesDeleted);

        Assert.True(File.Exists(Path.Combine(_root, "listings", "example.json")));
        Assert.True(File.Exists(Path.Combine(_root, "releases", "example@1.0.0.json")));

        // And the commit contains them, rather than an index describing documents that are gone.
        var tracked = Run("ls-files");

        Assert.Contains("listings/example.json", tracked, StringComparison.Ordinal);
        Assert.Contains("releases/example@1.0.0.json", tracked, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_document_that_leaves_the_export_leaves_the_mirror()
    {
        // The other direction has to keep working: withdrawing a listing means its document stops
        // being published, and a mirror that keeps serving it has not honoured the withdrawal.
        var mirror = new GitMirror(new GitMirrorOptions
        {
            WorkingDirectory = _root.Replace('\\', '/'),
            Push = false,
        });

        await mirror.SyncAsync(new ExportResult
        {
            Files =
            [
                new ExportFile("index.json", "{}"),
                new ExportFile("listings/gone.json", "{\"id\":\"Gone\"}"),
            ],
            Skipped = [],
        }, "first", CancellationToken.None);

        var outcome = await mirror.SyncAsync(new ExportResult
        {
            Files = [new ExportFile("index.json", "{}")],
            Skipped = [],
        }, "second", CancellationToken.None);

        Assert.Equal(1, outcome.FilesDeleted);
        Assert.False(File.Exists(Path.Combine(_root, "listings", "gone.json")));
    }

    [Fact]
    public void A_run_that_changes_only_the_timestamp_is_not_a_change()
    {
        // Otherwise the exporter commits every fifteen minutes forever and the history - the thing
        // that makes a git mirror worth more than a tarball - becomes noise.
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.json"),
            "{\"generated_at\": \"2026-01-01T00:00:00Z\", \"listings\": 1}");

        var later = new ExportResult
        {
            Files = [new ExportFile("index.json", "{\"generated_at\": \"2026-06-01T12:00:00Z\", \"listings\": 1}")],
            Skipped = [],
        };

        Assert.False(ExportComparer.DiffersFrom(later, _root));

        var actuallyDifferent = new ExportResult
        {
            Files = [new ExportFile("index.json", "{\"generated_at\": \"2026-06-01T12:00:00Z\", \"listings\": 2}")],
            Skipped = [],
        };

        Assert.True(ExportComparer.DiffersFrom(actuallyDifferent, _root));
    }

    [Fact]
    public void A_new_document_is_a_change_even_when_nothing_else_moved()
    {
        File.WriteAllText(Path.Combine(_root, "index.json"), "{\"generated_at\":\"x\"}");

        var withListing = new ExportResult
        {
            Files =
            [
                new ExportFile("index.json", "{\"generated_at\":\"y\"}"),
                new ExportFile("listings/new.json", "{}"),
            ],
            Skipped = [],
        };

        Assert.True(ExportComparer.DiffersFrom(withListing, _root));
    }

    [Fact]
    public async Task An_empty_volume_becomes_something_the_mirror_can_commit_into()
    {
        // The state of a fresh deployment. Nothing else creates a git tree, so without this the
        // service comes up healthy and fails on every run - the failure mode a backup must not
        // have.
        var fresh = Path.Combine(Path.GetTempPath(), $"ksamods-fresh-{Guid.NewGuid():N}");

        try
        {
            await MirrorBootstrap.PrepareAsync(fresh, remoteUrl: null, "main", CancellationToken.None);

            Assert.True(Directory.Exists(Path.Combine(fresh, ".git")));

            var mirror = new GitMirror(new GitMirrorOptions { WorkingDirectory = fresh, Push = false });

            var outcome = await mirror.SyncAsync(new ExportResult
            {
                Files = [new ExportFile("index.json", "{}")],
                Skipped = [],
            }, "first", CancellationToken.None);

            Assert.True(outcome.Changed);
        }
        finally
        {
            Delete(fresh);
        }
    }

    [Fact]
    public async Task Preparing_an_already_prepared_directory_changes_nothing()
    {
        // It runs every pass, not only the first, so it has to be safe to repeat.
        await MirrorBootstrap.PrepareAsync(_root, remoteUrl: null, "main", CancellationToken.None);
        await MirrorBootstrap.PrepareAsync(_root, remoteUrl: null, "main", CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(_root, ".git")));
    }

    [Fact]
    public void A_push_url_is_redacted_before_it_reaches_a_log()
    {
        // These carry a token. A deploy log is the first thing somebody pastes into a chat when
        // asking why the mirror is not updating.
        Assert.Equal("https://github.com/org/index.git",
            MirrorBootstrap.Redact("https://x-access-token:ghp_secret@github.com/org/index.git"));

        // Nothing to hide, nothing hidden - and no crash on the forms that carry no credentials.
        Assert.Equal("https://github.com/org/index.git",
            MirrorBootstrap.Redact("https://github.com/org/index.git"));
        Assert.Equal("(none)", MirrorBootstrap.Redact(null));
        Assert.Equal("git@github.com:org/index.git", MirrorBootstrap.Redact("git@github.com:org/index.git"));
    }

    private static void Delete(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Run(params string[] arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }.With(arguments))!;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }
}

internal static class ProcessStartInfoExtensions
{
    public static System.Diagnostics.ProcessStartInfo With(
        this System.Diagnostics.ProcessStartInfo info, params string[] arguments)
    {
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }
}
