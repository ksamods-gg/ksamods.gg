namespace KsaMods.Exporter;

/// <summary>
/// Decides whether an export run is worth committing.
///
/// <para>This is the second half of spec/snapshot.md's determinism rule: the builder writes the
/// same bytes for the same input, and an index whose bytes are already published is not published
/// again. Both are needed, because publishing is a deployment and a deployment issues a new ETag
/// either way. Without it the exporter would commit every fifteen minutes forever and the mirror's
/// history - the thing that makes a git mirror worth having over a tarball - would be noise with
/// the real changes buried in it.</para>
///
/// <para>It used to blank a <c>generated_at</c> field before comparing, because the snapshot
/// carried one. The spec now forbids that field outright, for exactly the reason the workaround
/// existed, so there is nothing left to blank.</para>
/// </summary>
public static class ExportComparer
{
    /// <summary>
    /// Whether the built export differs from what is already on disk, in any way that matters.
    ///
    /// <para>Errs towards true: a working directory it cannot read is a reason to write, not a
    /// reason to assume everything is fine. The cost of a wrong "yes" is one empty commit; the cost
    /// of a wrong "no" is an index that silently stops updating.</para>
    /// </summary>
    public static bool DiffersFrom(ExportResult export, string workingDirectory)
    {
        if (!Directory.Exists(workingDirectory)) return true;

        try
        {
            var built = export.Files.ToDictionary(
                f => Normalise(f.Path), f => f.Content, StringComparer.Ordinal);

            var onDisk = Directory
                .EnumerateFiles(workingDirectory, "*.json", SearchOption.AllDirectories)
                .Where(p => !IsInsideGit(workingDirectory, p))
                .ToDictionary(p => Normalise(Path.GetRelativePath(workingDirectory, p)), StringComparer.Ordinal);

            // A file the export no longer produces is a change too - a delisted mod's document has
            // to leave the mirror, and that is the whole point of the withdrawal.
            if (built.Count != onDisk.Count) return true;

            foreach (var (path, content) in built)
            {
                if (!onDisk.TryGetValue(path, out var existing)) return true;

                if (!Matches(File.ReadAllText(existing), content)) return true;
            }

            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool Matches(string existing, string built) =>
        string.Equals(existing, built, StringComparison.Ordinal);

    private static string Normalise(string path) => path.Replace('\\', '/');

    private static bool IsInsideGit(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/').StartsWith(".git/", StringComparison.Ordinal);
}
