namespace KsaMods.Metadata;

public enum Severity
{
    Info,
    Warning,
    Error,
}

public enum ValidationOutcome
{
    Passed,
    PassedWithWarnings,
    Failed,
}

/// <summary>
/// One validation result.
///
/// <para><b>The <see cref="Code"/> is the contract</b> (backend.md §7.4). It is stored in the
/// database, printed by the CLI, returned by the API and carried in the export. A
/// <see cref="Message"/> can be reworded freely; a code cannot, because things filter on it.</para>
/// </summary>
public sealed record Finding(
    int Stage,
    Severity Severity,
    string Code,
    string Message,
    string? Path = null);

/// <summary>
/// Stable finding codes. Numbered <c>KSAM-SSNN</c> where SS is the pipeline stage.
/// Never renumber one: add a new code and retire the old.
/// </summary>
public static class FindingCodes
{
    // ── Stage 1: fetch ──
    public const string FetchFailed = "KSAM-0101";
    public const string FetchTooLarge = "KSAM-0102";
    public const string FetchHostNotAllowed = "KSAM-0103";

    // ── Stage 2: integrity ──
    public const string HashMismatch = "KSAM-0201";

    // ── Stage 3: archive safety ──
    public const string ArchiveCorrupt = "KSAM-0301";
    public const string PathTraversal = "KSAM-0302";
    public const string AbsolutePath = "KSAM-0303";
    public const string NonRegularEntry = "KSAM-0304";
    public const string NestedArchive = "KSAM-0305";
    public const string CompressionRatioExceeded = "KSAM-0306";
    public const string UncompressedSizeExceeded = "KSAM-0307";
    public const string EntryCountExceeded = "KSAM-0308";
    public const string JunkFileStripped = "KSAM-0309";
    public const string ReservedFilePresent = "KSAM-0310";

    // ── Stage 4: structure ──
    public const string NotExactlyOneRoot = "KSAM-0401";
    public const string RootNameMismatch = "KSAM-0402";
    public const string ModTomlMissing = "KSAM-0403";
    public const string ModTomlUnparseable = "KSAM-0404";

    // ── Stage 5: declaration integrity ──
    public const string DeclaredPathMissing = "KSAM-0501";
    public const string DeclaredXmlUnparseable = "KSAM-0502";
    public const string UnreachableContentFile = "KSAM-0503";
    public const string WrongXmlRoot = "KSAM-0504";
    public const string ReferencedPathMissing = "KSAM-0505";

    // ── Stage 6: asset ids ──
    public const string AssetIdUnprefixed = "KSAM-0601";
    public const string AssetIdDuplicateWithinMod = "KSAM-0602";

    // ── Stage 7: code facet ──
    public const string EntryAssemblyMissing = "KSAM-0701";
    public const string GameAssemblyShipped = "KSAM-0702";
    public const string LoaderAssemblyShipped = "KSAM-0703";
    public const string BuildArtefactShipped = "KSAM-0704";
    public const string AssemblyUnreadable = "KSAM-0705";

    // ── Stage 7b: dependencies ──
    public const string DependencyMalformed = "KSAM-0751";

    // ── Stage 8: risk surface ──
    public const string ConsoleBlockPresent = "KSAM-0801";
    public const string CoreIdOverride = "KSAM-0802";
}
