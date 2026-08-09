using System.Diagnostics;
using System.Text;

namespace KsaMods.Exporter;

/// <summary>
/// Gets the working directory into a state <see cref="GitMirror"/> can commit into.
///
/// <para>Without this the exporter fails on every run of a fresh deployment: the volume is an
/// empty directory, and a mirror needs a git tree. Nothing else creates one, so the service would
/// come up healthy and never produce a commit - the failure mode a backup must not have.</para>
///
/// <para>The remote is authoritative. If it has the branch already, the local tree is reset to it
/// rather than diverging: everything in the mirror is derived from the database, so discarding
/// unpushed local commits loses nothing that the next run will not rebuild, while a diverged
/// history needs somebody to resolve it by hand at exactly the moment nobody is watching.</para>
/// </summary>
public static class MirrorBootstrap
{
    public static async Task PrepareAsync(
        string directory, string? remoteUrl, string branch, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);

        if (!Directory.Exists(Path.Combine(directory, ".git")))
        {
            await GitAsync(directory, ct, "init", "--initial-branch", branch);

            // An identity, so `git commit` works in a container where nothing is configured
            // globally. GitMirror passes its own per-commit, but git still refuses some operations
            // without one set.
            await GitAsync(directory, ct, "config", "user.name", "ksamods exporter");
            await GitAsync(directory, ct, "config", "user.email", "exporter@ksamods.gg");
        }

        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            // Local-only. Valid for a dry run or a deployment that has not decided where the
            // mirror lives yet - the commits are real, they just have nowhere to go.
            return;
        }

        var remotes = await GitAsync(directory, ct, "remote");

        await (remotes.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                      .Any(r => r.Trim() == "origin")
            // set-url rather than add, so changing where the mirror lives is a variable change
            // rather than a manual fix inside a volume.
            ? GitAsync(directory, ct, "remote", "set-url", "origin", remoteUrl)
            : GitAsync(directory, ct, "remote", "add", "origin", remoteUrl));

        var fetched = await TryGitAsync(directory, ct, "fetch", "--quiet", "origin", branch);

        if (!fetched)
        {
            // An empty repository has no branch to fetch, which is the normal state of a mirror
            // on its first day. Not an error: the first push creates it.
            return;
        }

        await GitAsync(directory, ct, "reset", "--hard", $"origin/{branch}");
    }

    /// <summary>Hides the credentials a push URL usually carries, so a deploy log stays shareable.</summary>
    public static string Redact(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "(none)";

        var at = url.LastIndexOf('@');
        var scheme = url.IndexOf("//", StringComparison.Ordinal);

        return at > 0 && scheme > 0 && at > scheme
            ? string.Concat(url.AsSpan(0, scheme + 2), url.AsSpan(at + 1))
            : url;
    }

    private static async Task<string> GitAsync(string directory, CancellationToken ct, params string[] arguments)
    {
        var (exitCode, stdout, stderr) = await RunAsync(directory, ct, arguments);

        if (exitCode != 0)
        {
            throw new GitMirrorException(
                $"git {string.Join(' ', arguments)} failed ({exitCode}).", stderr + stdout);
        }

        return stdout;
    }

    private static async Task<bool> TryGitAsync(string directory, CancellationToken ct, params string[] arguments)
    {
        var (exitCode, _, _) = await RunAsync(directory, ct, arguments);
        return exitCode == 0;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string directory, CancellationToken ct, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);

        // Never prompt. A credential prompt in a container hangs the service until the timeout
        // rather than failing with something anybody can read.
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.StartInfo.Environment["GIT_ASKPASS"] = "";

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
