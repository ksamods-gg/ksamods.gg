using System.Diagnostics;
using System.Text;

namespace KsaMods.Exporter;

public sealed record GitMirrorOptions
{
    /// <summary>A working clone of the public mirror repository.</summary>
    public required string WorkingDirectory { get; init; }

    public string Branch { get; init; } = "main";
    public string Remote { get; init; } = "origin";
    public string AuthorName { get; init; } = "ksamods exporter";
    public string AuthorEmail { get; init; } = "exporter@ksamods.gg";

    /// <summary>Set false in tests, or for a dry run against a local clone.</summary>
    public bool Push { get; init; } = true;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed record MirrorOutcome
{
    public required bool Changed { get; init; }
    public string? CommitSha { get; init; }
    public required int FilesWritten { get; init; }
    public required int FilesDeleted { get; init; }
}

public sealed class GitMirrorException(string message, string? output = null)
    : Exception(output is null ? message : $"{message}\n{output}");

/// <summary>
/// Writes the export into a git clone and pushes it (backend.md §12.1).
///
/// <para><b>This is required, not optional.</b> Postgres holds the record, so without the mirror
/// the catalogue does not outlive the service. A tarball behind a CDN dies with the CDN; a public
/// git repository survives because it gets forked - and forks happen before the outage, not
/// after. Nobody clones a backup they did not know existed.</para>
///
/// <para>It is a follower, not a reviewer: it inherits whatever the writer produced. It
/// guarantees the data outlives the service. It does not guarantee the data is right.</para>
/// </summary>
public sealed class GitMirror(GitMirrorOptions options)
{
    public async Task<MirrorOutcome> SyncAsync(ExportResult export, string message, CancellationToken ct)
    {
        var root = options.WorkingDirectory;
        if (!Directory.Exists(Path.Combine(root, ".git")))
        {
            throw new GitMirrorException($"'{root}' is not a git working tree.");
        }

        var written = await WriteFilesAsync(root, export, ct);
        var deleted = DeleteRemovedFiles(root, export);

        await GitAsync(ct, "add", "--all", ".");

        // --quiet exits non-zero when there is nothing staged, which is exactly the signal we
        // want: a run that changes nothing must produce no commit, or the history is noise.
        var status = await GitRawAsync(ct, "diff", "--cached", "--quiet");
        if (status.ExitCode == 0)
        {
            return new MirrorOutcome { Changed = false, FilesWritten = written, FilesDeleted = deleted };
        }

        await GitAsync(ct,
            "-c", $"user.name={options.AuthorName}",
            "-c", $"user.email={options.AuthorEmail}",
            "commit", "--message", message);

        var sha = (await GitAsync(ct, "rev-parse", "HEAD")).Trim();

        if (options.Push)
        {
            // A failed push is an alert, not a warning: a silently stale mirror is worse than no
            // mirror, because it looks like a backup.
            await GitAsync(ct, "push", options.Remote, $"HEAD:{options.Branch}");
        }

        return new MirrorOutcome
        {
            Changed = true,
            CommitSha = sha,
            FilesWritten = written,
            FilesDeleted = deleted,
        };
    }

    private static async Task<int> WriteFilesAsync(string root, ExportResult export, CancellationToken ct)
    {
        var written = 0;

        foreach (var file in export.Files)
        {
            var path = ResolveInside(root, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Only rewrite when the content actually differs, so mtimes stay put and a no-op run
            // touches nothing on disk either.
            if (File.Exists(path))
            {
                var existing = await File.ReadAllTextAsync(path, ct);
                if (string.Equals(existing, file.Content, StringComparison.Ordinal)) continue;
            }

            // No BOM, LF endings: the same source must produce the same bytes on every platform
            // the exporter might run on.
            await File.WriteAllTextAsync(path, file.Content, ExportEncoding.Utf8NoBom, ct);
            written++;
        }

        return written;
    }

    private static int DeleteRemovedFiles(string root, ExportResult export)
    {
        var expected = export.Files
            .Select(f => ResolveInside(root, f.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deleted = 0;

        foreach (var directory in new[] { "listings", "releases", "modlists" })
        {
            var path = Path.Combine(root, directory);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories))
            {
                // GetFullPath on both sides, because `expected` holds normalised paths and this
                // does not: EnumerateFiles prefixes results with the string it was given, so a
                // working directory written with forward slashes comes back with forward slashes
                // and matches nothing. The symptom is the mirror deleting every document it just
                // wrote, on every run, leaving an index that lists content it no longer carries.
                if (expected.Contains(Path.GetFullPath(file))) continue;

                File.Delete(file);
                deleted++;
            }

            foreach (var empty in Directory
                         .EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(empty).Any()) Directory.Delete(empty);
            }
        }

        return deleted;
    }

    /// <summary>
    /// Resolves an export-relative path inside the working tree, refusing anything that escapes.
    /// The exporter builds these paths itself, but they carry user-chosen ids, so the check costs
    /// nothing and closes the case where an id rule is one day loosened.
    /// </summary>
    private static string ResolveInside(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rooted = Path.GetFullPath(root);

        if (!full.StartsWith(rooted + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, rooted, StringComparison.OrdinalIgnoreCase))
        {
            throw new GitMirrorException($"Export path '{relative}' escapes the mirror working tree.");
        }

        return full;
    }

    private async Task<string> GitAsync(CancellationToken ct, params string[] arguments)
    {
        var result = await GitRawAsync(ct, arguments);
        if (result.ExitCode != 0)
        {
            throw new GitMirrorException($"git {string.Join(' ', arguments)} failed ({result.ExitCode}).",
                result.Stderr + result.Stdout);
        }
        return result.Stdout;
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> GitRawAsync(
        CancellationToken ct, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = options.WorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(options.Timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new GitMirrorException($"git {arguments.FirstOrDefault()} timed out.");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
