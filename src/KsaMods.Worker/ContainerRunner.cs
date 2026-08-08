using System.Diagnostics;
using System.Text;

namespace KsaMods.Worker;

public sealed record ContainerPolicy
{
    /// <summary>
    /// Pinned by digest, never by tag. A tag is mutable, and "the image we tested" is the whole
    /// point of pinning it.
    /// </summary>
    public required string ImageDigest { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);
    public string Memory { get; init; } = "1g";
    public string Cpus { get; init; } = "1.0";
    public int PidsLimit { get; init; } = 128;
    public string TmpfsSize { get; init; } = "512m";
    public string? SeccompProfilePath { get; init; }
}

public sealed record ContainerOutcome
{
    public required int ExitCode { get; init; }
    public required bool TimedOut { get; init; }
    public required string? ReportJson { get; init; }
    public required string Stderr { get; init; }
}

/// <summary>
/// Runs one validation job in its own container (backend.md §7.2).
///
/// <para><b>This type is why the worker needs its own host.</b> It talks to the Docker daemon,
/// which is root-equivalent, and it does so on behalf of bytes a stranger produced. The socket is
/// never mounted into the container — the container has no idea a daemon exists.</para>
/// </summary>
public sealed class ContainerRunner(ContainerPolicy policy)
{
    public async Task<ContainerOutcome> RunAsync(
        string archivePath, string expectedModId, CancellationToken ct)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"ksamods-out-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            var arguments = BuildArguments(archivePath, outputDirectory, expectedModId);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);

            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(policy.Timeout + TimeSpan.FromSeconds(15));

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                TryKill(process);
            }

            var reportPath = Path.Combine(outputDirectory, "report.json");
            var report = File.Exists(reportPath) ? await File.ReadAllTextAsync(reportPath, ct) : null;

            return new ContainerOutcome
            {
                ExitCode = timedOut ? -1 : process.ExitCode,
                TimedOut = timedOut,
                ReportJson = report,
                Stderr = stderr.ToString(),
            };
        }
        finally
        {
            try { Directory.Delete(outputDirectory, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    /// <summary>
    /// The flags are the sandbox. A Dockerfile cannot express any of them, so this list is the
    /// actual security boundary and every entry is load-bearing.
    /// </summary>
    internal IReadOnlyList<string> BuildArguments(
        string archivePath, string outputDirectory, string expectedModId)
    {
        var args = new List<string>
        {
            "run",
            "--rm",

            // No egress, no DNS, no cloud metadata endpoint. The single most important flag here:
            // it is what makes a defeated parser worthless rather than a pivot.
            "--network", "none",

            "--read-only",
            $"--tmpfs=/tmp:rw,noexec,nosuid,size={policy.TmpfsSize}",

            // The chiselled base image's non-root `app` user. Must stay in step with the USER
            // line in the Dockerfile. The image ships no setuid binaries, so even if this were
            // subverted there is nothing to escalate to.
            "--user", "64198:64198",

            "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges",

            $"--pids-limit={policy.PidsLimit}",

            // Equal memory and memory-swap means no swap: an over-allocating job is OOM-killed
            // rather than dragging the host into thrashing.
            $"--memory={policy.Memory}",
            $"--memory-swap={policy.Memory}",
            $"--cpus={policy.Cpus}",
            "--ulimit", "nofile=256:256",

            // Belt and braces alongside the host-side timeout in RunAsync.
            $"--stop-timeout={(int)policy.Timeout.TotalSeconds}",
        };

        if (policy.SeccompProfilePath is { } seccomp)
        {
            args.Add("--security-opt");
            args.Add($"seccomp={seccomp}");
        }

        // The archive goes in read-only; /out is the only writable mount.
        args.Add("-v");
        args.Add($"{archivePath}:/in/archive.zip:ro");
        args.Add("-v");
        args.Add($"{outputDirectory}:/out");

        args.Add(policy.ImageDigest);
        args.Add(expectedModId);

        return args;
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* already exited */ }
    }
}
