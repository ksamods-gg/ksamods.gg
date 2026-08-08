using System.IO.Compression;
using KsaMods.Metadata;

namespace KsaMods.Validation;

/// <summary>Ceilings for archive expansion. All four matter: a zip bomb defeats any three alone.</summary>
public sealed record ArchiveLimits
{
    public long MaxCompressedBytes { get; init; } = 50L * 1024 * 1024;
    public long MaxUncompressedBytes { get; init; } = 500L * 1024 * 1024;
    public long MaxEntryUncompressedBytes { get; init; } = 200L * 1024 * 1024;
    public int MaxEntries { get; init; } = 10_000;
    public double MaxCompressionRatio { get; init; } = 100.0;

    public static ArchiveLimits Default { get; } = new();
}

public sealed record ArchiveEntry
{
    /// <summary>Normalised, forward-slashed, relative path. Safe to compare; never safe to write to disk unchecked.</summary>
    public required string Path { get; init; }
    public required long CompressedSize { get; init; }
    public required long UncompressedSize { get; init; }
    public required bool IsDirectory { get; init; }

    public string FileName => Path[(Path.LastIndexOf('/') + 1)..];

    public bool HasExtension(string ext) =>
        Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads a zip entry-by-entry with every guard from backend.md §14.2 applied before any entry is
/// exposed. Nothing here writes to disk — the validator works entirely in memory, so the classic
/// zip-slip write primitive does not exist in this codebase at all.
/// </summary>
public sealed class SafeArchive : IDisposable
{
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _byPath;

    public IReadOnlyList<ArchiveEntry> Entries { get; }
    public IReadOnlyList<Finding> Findings { get; }
    public long TotalUncompressedBytes { get; }

    /// <summary>True when a guard rejected the archive outright and no entries should be trusted.</summary>
    public bool Rejected { get; }

    private static readonly string[] JunkPrefixes = ["__MACOSX/"];
    private static readonly string[] JunkNames = [".DS_Store", "Thumbs.db", "desktop.ini"];

    /// <summary>
    /// Files that are meaningful only outside a mod folder. An archive carrying one is malformed
    /// (spec §8): manifest.toml is the game's enabled-mods list and lives in the documents root.
    /// </summary>
    private static readonly string[] ReservedNames = ["manifest.toml", "settings.toml"];

    private static readonly string[] NestedArchiveExtensions =
        [".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz"];

    private SafeArchive(
        ZipArchive zip,
        Dictionary<string, ZipArchiveEntry> byPath,
        List<ArchiveEntry> entries,
        List<Finding> findings,
        long totalUncompressed,
        bool rejected)
    {
        _zip = zip;
        _byPath = byPath;
        Entries = entries;
        Findings = findings;
        TotalUncompressedBytes = totalUncompressed;
        Rejected = rejected;
    }

    public static SafeArchive Open(Stream stream, ArchiveLimits? limits = null)
    {
        limits ??= ArchiveLimits.Default;
        var findings = new List<Finding>();
        var entries = new List<ArchiveEntry>();
        var byPath = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            findings.Add(new Finding(3, Severity.Error, FindingCodes.ArchiveCorrupt,
                $"The archive could not be opened: {ex.Message}"));
            return new SafeArchive(
                new ZipArchive(new MemoryStream(), ZipArchiveMode.Read),
                byPath, entries, findings, 0, rejected: true);
        }

        var rejected = false;
        long totalUncompressed = 0;
        long totalCompressed = 0;

        if (zip.Entries.Count > limits.MaxEntries)
        {
            findings.Add(new Finding(3, Severity.Error, FindingCodes.EntryCountExceeded,
                $"The archive contains {zip.Entries.Count} entries; the limit is {limits.MaxEntries}."));
            rejected = true;
        }

        foreach (var raw in zip.Entries)
        {
            var name = raw.FullName;

            // An entry name is untrusted for the filesystem, for logs and for the database.
            // Normalise separators before any comparison so \ and / cannot smuggle a segment past.
            var normalised = name.Replace('\\', '/');

            if (IsJunk(normalised))
            {
                findings.Add(new Finding(3, Severity.Info, FindingCodes.JunkFileStripped,
                    "Archiver metadata stripped.", normalised));
                continue;
            }

            if (System.IO.Path.IsPathRooted(normalised) || normalised.Length > 1 && normalised[1] == ':')
            {
                findings.Add(new Finding(3, Severity.Error, FindingCodes.AbsolutePath,
                    "Entry has an absolute path.", normalised));
                rejected = true;
                continue;
            }

            if (EscapesRoot(normalised))
            {
                findings.Add(new Finding(3, Severity.Error, FindingCodes.PathTraversal,
                    "Entry path escapes the archive root.", normalised));
                rejected = true;
                continue;
            }

            // Zip stores unix mode in the high 16 bits of ExternalAttributes. S_IFLNK is 0xA000;
            // anything that is not a regular file or a directory is refused rather than
            // interpreted, because the alternatives are all worse than a rejected upload.
            if (IsNonRegular(raw))
            {
                findings.Add(new Finding(3, Severity.Error, FindingCodes.NonRegularEntry,
                    "Entry is a symlink, hardlink or device node.", normalised));
                rejected = true;
                continue;
            }

            var isDirectory = normalised.EndsWith('/') || raw.Length == 0 && raw.Name.Length == 0;

            if (!isDirectory && IsNestedArchive(normalised))
            {
                findings.Add(new Finding(3, Severity.Error, FindingCodes.NestedArchive,
                    "Nested archives are not permitted.", normalised));
                rejected = true;
                continue;
            }

            if (!isDirectory && IsReserved(normalised))
            {
                findings.Add(new Finding(3, Severity.Error, FindingCodes.ReservedFilePresent,
                    $"'{System.IO.Path.GetFileName(normalised)}' is the game's own file and must not appear inside a mod archive.",
                    normalised));
                rejected = true;
                continue;
            }

            if (raw.Length > limits.MaxEntryUncompressedBytes)
            {
                findings.Add(new Finding(3, Severity.Error, FindingCodes.UncompressedSizeExceeded,
                    $"Entry expands to {raw.Length} bytes; the per-entry limit is {limits.MaxEntryUncompressedBytes}.",
                    normalised));
                rejected = true;
                continue;
            }

            totalUncompressed += raw.Length;
            totalCompressed += raw.CompressedLength;

            if (totalUncompressed > limits.MaxUncompressedBytes)
            {
                findings.Add(new Finding(3, Severity.Error, FindingCodes.UncompressedSizeExceeded,
                    $"The archive expands past the {limits.MaxUncompressedBytes} byte limit."));
                rejected = true;
                break;
            }

            var entry = new ArchiveEntry
            {
                Path = normalised,
                CompressedSize = raw.CompressedLength,
                UncompressedSize = raw.Length,
                IsDirectory = isDirectory,
            };

            entries.Add(entry);
            if (!isDirectory) byPath[normalised] = raw;
        }

        // Ratio is checked on the totals rather than per entry: a single highly-compressible
        // file is normal, an archive that is *entirely* highly-compressible is not.
        if (!rejected && totalCompressed > 0)
        {
            var ratio = (double)totalUncompressed / totalCompressed;
            if (ratio > limits.MaxCompressionRatio)
            {
                findings.Add(new Finding(3, Severity.Error, FindingCodes.CompressionRatioExceeded,
                    $"Compression ratio {ratio:F1}:1 exceeds the {limits.MaxCompressionRatio:F0}:1 limit."));
                rejected = true;
            }
        }

        return new SafeArchive(zip, byPath, entries, findings, totalUncompressed, rejected);
    }

    /// <summary>
    /// Opens an entry for reading, capped at its declared uncompressed size so a lying central
    /// directory cannot make a reader allocate without bound.
    /// </summary>
    public Stream Open(ArchiveEntry entry)
    {
        if (!_byPath.TryGetValue(entry.Path, out var raw))
        {
            throw new InvalidOperationException($"'{entry.Path}' is not a readable entry in this archive.");
        }
        return new BoundedStream(raw.Open(), entry.UncompressedSize);
    }

    public bool TryGet(string path, out ArchiveEntry entry)
    {
        foreach (var candidate in Entries)
        {
            if (!candidate.IsDirectory && string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                entry = candidate;
                return true;
            }
        }
        entry = null!;
        return false;
    }

    public string ReadAllText(ArchiveEntry entry)
    {
        using var s = Open(entry);
        using var reader = new StreamReader(s, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static bool IsJunk(string path)
    {
        foreach (var prefix in JunkPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        var name = path[(path.LastIndexOf('/') + 1)..];
        foreach (var junk in JunkNames)
        {
            if (string.Equals(name, junk, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsReserved(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        foreach (var reserved in ReservedNames)
        {
            if (string.Equals(name, reserved, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsNestedArchive(string path)
    {
        foreach (var ext in NestedArchiveExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool EscapesRoot(string path)
    {
        var depth = 0;
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                depth--;
                if (depth < 0) return true;
                continue;
            }
            depth++;
        }
        return false;
    }

    private static bool IsNonRegular(ZipArchiveEntry entry)
    {
        // For a unix-produced zip the external attributes carry the st_mode in their high 16
        // bits. The host-OS indicator lives in the "version made by" header field, which .NET
        // does not surface — so instead of guessing the producer, read the file-type nibble and
        // treat an unset one as "regular". A Windows-produced entry stores DOS attributes in the
        // low byte and leaves the high half zero, which lands on exactly that case.
        var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        var fileType = unixMode & 0xF000;

        const int SIfReg = 0x8000;  // regular file
        const int SIfDir = 0x4000;  // directory

        if (fileType == 0) return false;
        return fileType is not (SIfReg or SIfDir);
    }

    public void Dispose() => _zip.Dispose();
}

/// <summary>Caps a decompression stream at the size the central directory promised.</summary>
internal sealed class BoundedStream(Stream inner, long limit) : Stream
{
    private long _read;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => limit;
    public override long Position { get => _read; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_read >= limit) return 0;
        var allowed = (int)Math.Min(count, limit - _read);
        var n = inner.Read(buffer, offset, allowed);
        _read += n;
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
