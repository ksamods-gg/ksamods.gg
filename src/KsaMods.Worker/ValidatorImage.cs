using System.Diagnostics;
using System.Text;

namespace KsaMods.Worker;

/// <summary>
/// Works out which image every validation run will use, once, at startup.
///
/// <para>Pinning matters because a tag is mutable and "the image we tested" is the whole point of
/// pinning one. But making a human find a digest before the first deploy is the kind of step that
/// gets skipped, worked around, or pinned once and never updated - so the deployment builds the
/// image under a tag and this resolves that tag to the immutable id it currently points at.</para>
///
/// <para>The property that actually matters survives: every run in this worker's lifetime uses one
/// image that cannot change under it, and the log says which. A redeploy resolves again.</para>
/// </summary>
public static class ValidatorImage
{
    /// <summary>
    /// A tag put on whichever image id this worker pinned, so that image stops being dangling.
    ///
    /// <para>This is what keeps the pin from evaporating. Rebuilding the validator moves the
    /// build tag to a new id and leaves the old one - the one a running worker is still pinned to -
    /// untagged, and the next <c>docker image prune</c> deletes it. The worker then cannot validate
    /// anything until it restarts. A tag makes the image not dangling, and prune leaves it alone.</para>
    ///
    /// <para>One fixed name rather than one per process: every worker on a host resolves the same
    /// configured tag to the same id, so they all point this at the same place, and a restart moves
    /// it rather than accumulating tags nobody removes.</para>
    /// </summary>
    public const string InUseTag = "ksamods-validator:in-use";

    /// <summary>
    /// Returns the reference to run, or null with a reason written to <paramref name="error"/>.
    ///
    /// <para>An explicit digest is used verbatim and never resolved: somebody who pinned one meant
    /// it, and looking it up locally could quietly substitute a different image of the same
    /// name.</para>
    /// </summary>
    public static async Task<string?> ResolveAsync(
        string? explicitDigest, string? tag, Action<string> log, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(explicitDigest))
        {
            if (!explicitDigest.Contains("@sha256:", StringComparison.Ordinal))
            {
                log($"Validator__ImageDigest is '{explicitDigest}', which is a tag rather than a digest. "
                  + "Use Validator__Image for a tag - it is resolved to an id at startup - or give a "
                  + "reference of the form name@sha256:...");
                return null;
            }

            return explicitDigest;
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            log("Set Validator__Image to the validator image, or Validator__ImageDigest to pin one "
              + "exactly. Refusing to start without a validator: a worker that cannot sandbox is "
              + "worse than no worker.");
            return null;
        }

        var resolved = await InspectAsync(tag, ct);

        if (resolved is null)
        {
            // Not present locally. Normal for a registry image on a fresh host, and hopeless for
            // one the deployment was supposed to build - the pull failing says which.
            log($"'{tag}' is not on this host; pulling.");

            if (!await PullAsync(tag, ct))
            {
                log($"Could not find or pull '{tag}'. If the deployment builds it, check that the "
                  + "validator service ran before this one.");
                return null;
            }

            resolved = await InspectAsync(tag, ct);
        }

        if (resolved is null)
        {
            log($"'{tag}' could not be resolved to an image id.");
            return null;
        }

        log($"Validator image {tag} resolved to {Short(resolved)}. Every run uses that id, so a "
          + "later push to the same tag cannot change what this worker executes.");

        // Best effort. Failing to tag costs the prune protection, not the run, and refusing to
        // start over it would trade a rare fault for a certain one.
        if (await TagAsync(resolved, InUseTag, ct))
        {
            log($"Tagged it {InUseTag} so rebuilding {tag} cannot leave this id dangling for "
              + "docker image prune to collect while it is still in use.");
        }
        else
        {
            log($"Could not tag {Short(resolved)} as {InUseTag}. It still runs; it is just prunable "
              + "if the validator is rebuilt before this worker restarts.");
        }

        return resolved;
    }

    private static string Short(string id) =>
        id.StartsWith("sha256:", StringComparison.Ordinal) && id.Length > 19 ? id[..19] : id;

    private static async Task<string?> InspectAsync(string reference, CancellationToken ct)
    {
        var (exitCode, stdout, _) = await DockerAsync(ct, "image", "inspect", reference, "--format", "{{.Id}}");

        if (exitCode != 0) return null;

        var id = stdout.Trim();

        // A daemon that answers something other than an id is a daemon to distrust, not to guess at.
        return id.StartsWith("sha256:", StringComparison.Ordinal) ? id : null;
    }

    private static async Task<bool> TagAsync(string id, string tag, CancellationToken ct)
    {
        var (exitCode, _, _) = await DockerAsync(ct, "tag", id, tag);
        return exitCode == 0;
    }

    private static async Task<bool> PullAsync(string reference, CancellationToken ct)
    {
        var (exitCode, _, _) = await DockerAsync(ct, "pull", "--quiet", reference);
        return exitCode == 0;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> DockerAsync(
        CancellationToken ct, params string[] arguments)
    {
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

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(5));

            await process.WaitForExitAsync(cts.Token);

            return (process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (Exception e) when (e is OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            // No docker on PATH, or no socket. Either way this worker cannot validate anything,
            // and the caller turns that into a refusal to start rather than a silent stall.
            return (-1, "", e.Message);
        }
    }
}
