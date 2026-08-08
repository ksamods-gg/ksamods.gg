using System.Text.RegularExpressions;

namespace KsaMods.Exporter;

/// <summary>
/// Decides whether an export run is worth committing.
///
/// <para><c>index.json</c> carries a <c>generated_at</c> timestamp, so a naive comparison says
/// "changed" on every run: the exporter would commit every fifteen minutes forever, the mirror
/// would grow without bound, and its history - the thing that makes a git mirror worth having over
/// a tarball - would be noise with the real changes buried in it.</para>
///
/// <para>So the timestamp is ignored when asking whether anything happened, and kept when
/// something did. "Nothing changed" then means what it says.</para>
/// </summary>
public static partial class ExportComparer
{
    [GeneratedRegex("\"generated_at\"\\s*:\\s*\"[^\"]*\"")]
    private static partial Regex GeneratedAt { get; }

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
        string.Equals(Blank(existing), Blank(built), StringComparison.Ordinal);

    /// <summary>
    /// Blanks the one field that moves on its own. Applied to every file rather than only
    /// index.json, so a timestamp added to another document later does not quietly reintroduce
    /// the commit-every-run behaviour.
    /// </summary>
    private static string Blank(string content) =>
        GeneratedAt.Replace(content, "\"generated_at\":\"\"");

    private static string Normalise(string path) => path.Replace('\\', '/');

    private static bool IsInsideGit(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/').StartsWith(".git/", StringComparison.Ordinal);
}
