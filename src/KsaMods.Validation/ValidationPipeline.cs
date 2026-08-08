using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using KsaMods.Metadata;

namespace KsaMods.Validation;

public sealed record ValidationRequest
{
    public required Stream Archive { get; init; }

    /// <summary>The id the listing claims. Stage 4 compares the archive's root against it.</summary>
    public required string ExpectedId { get; init; }

    public ArchiveLimits Limits { get; init; } = ArchiveLimits.Default;

    /// <summary>Asset ids declared by the game's own Core content, for override detection (stage 8).</summary>
    public IReadOnlySet<string>? CoreAssetIds { get; init; }
}

/// <summary>
/// Stages 3 to 8 of backend.md §7.3. Stages 1 and 2 — fetch and hash — happen on the worker,
/// outside the container, because SSRF is a network-policy problem and this is a parsing one.
///
/// <para>This type does no I/O: no HttpClient, no File, no database. The caller supplies bytes.
/// That is what makes the container, the CLI and any preflight run identical rules by
/// construction rather than by discipline.</para>
///
/// <para>The pipeline is <b>total, not fail-fast</b>: a stage records findings and continues
/// wherever continuing is meaningful, so an author gets every problem in one run instead of one
/// per push. Only genuinely uninterpretable input short-circuits.</para>
/// </summary>
public static class ValidationPipeline
{
    private static readonly string[] GameAssemblyPrefixes = ["KSA.", "Brutal.", "Planet."];
    private static readonly string[] GameAssemblyExact = ["KSA.dll"];
    private static readonly string[] LoaderAssemblyExact = ["StarMap.API.dll", "StarMap.Core.dll", "0Harmony.dll"];
    private static readonly string[] BuildArtefactExtensions = [".pdb", ".deps.json"];

    /// <summary>Content that is normally referenced from XML rather than declared in mod.toml.</summary>
    private static readonly string[] ContentExtensions =
    [
        ".glb", ".gltf", ".ktx2", ".png", ".dds", ".jpg", ".jpeg", ".tga",
        ".ogg", ".wav", ".ttf", ".otf",
        ".vert", ".frag", ".comp", ".glsl", ".bin", ".xml",
    ];

    public static ValidationResult Validate(ValidationRequest request)
    {
        var findings = new List<Finding>();

        // ── Stage 3: archive safety ──
        using var archive = SafeArchive.Open(request.Archive, request.Limits);
        findings.AddRange(archive.Findings);

        if (archive.Rejected)
        {
            return Finish(findings, new ExtractedFacts { UncompressedSize = archive.TotalUncompressedBytes });
        }

        // ── Stage 4: structure and install root ──
        var structure = ValidateStructure(archive, request.ExpectedId, findings);
        if (structure is null)
        {
            return Finish(findings, new ExtractedFacts { UncompressedSize = archive.TotalUncompressedBytes });
        }

        var (root, modToml) = structure.Value;

        // ── Stage 5: declaration integrity, then two-hop reachability ──
        var (declared, reachable, assetIds) = ValidateDeclarations(archive, root, modToml, findings);

        // ── Stage 5 (continued): orphan detection over the reachability set ──
        ReportUnreachable(archive, root, reachable, findings);

        // ── Stage 7: code facet ──
        var assemblies = InspectAssemblies(archive, root, request.ExpectedId, modToml, findings);

        // ── Stage 7b: dependency extraction ──
        // No validation beyond shape: the loader will act on these whatever the index thinks,
        // so they are recorded rather than judged.
        var dependencies = modToml.ModDependencies;

        // ── Stage 8: risk surface ──
        var console = CollectConsole(modToml, findings);
        ReportCoreOverrides(assetIds, request.CoreAssetIds, findings);

        var facts = new ExtractedFacts
        {
            InstallRoot = root,
            InstallRootDerived = true,
            AssetIds = assetIds,
            Dependencies = dependencies,
            Assemblies = assemblies,
            ConsoleCommands = console,
            DeclaredPaths = declared,
            ReachablePaths = [.. reachable],
            UncompressedSize = archive.TotalUncompressedBytes,
            EntryAssembly = modToml.EntryAssembly,
            ModName = modToml.Name,
            CommunityVersion = modToml.CommunityVersion,
            CommunityAuthor = modToml.CommunityAuthor,
        };

        return Finish(findings, facts);
    }

    private static ValidationResult Finish(List<Finding> findings, ExtractedFacts facts)
    {
        var outcome = findings.Any(f => f.Severity == Severity.Error)
            ? ValidationOutcome.Failed
            : findings.Any(f => f.Severity == Severity.Warning)
                ? ValidationOutcome.PassedWithWarnings
                : ValidationOutcome.Passed;

        return new ValidationResult { Outcome = outcome, Findings = findings, Facts = facts };
    }

    // ────────────────────────── Stage 4 ──────────────────────────

    private static (string Root, ModToml Toml)? ValidateStructure(
        SafeArchive archive, string expectedId, List<Finding> findings)
    {
        var roots = archive.Entries
            .Select(e => e.Path.Split('/', 2)[0])
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (roots.Count != 1)
        {
            findings.Add(new Finding(4, Severity.Error, FindingCodes.NotExactlyOneRoot,
                roots.Count == 0
                    ? "The archive is empty."
                    : $"The archive must contain exactly one top-level directory; found {roots.Count}: {string.Join(", ", roots.Take(5))}."));
            return null;
        }

        var root = roots[0];

        // Case-insensitive comparison, because that is how the namespace compares — but the
        // mismatch itself is fatal: the folder name is the identity the game will see, and no
        // amount of correct metadata survives getting it wrong.
        if (!string.Equals(root, expectedId, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new Finding(4, Severity.Error, FindingCodes.RootNameMismatch,
                $"The archive's root directory is '{root}' but this listing's id is '{expectedId}'. " +
                "The folder name is the mod's identity in-game, so these must match."));
            return null;
        }

        var modTomlPath = $"{root}/mod.toml";
        if (!archive.TryGet(modTomlPath, out var modTomlEntry))
        {
            findings.Add(new Finding(4, Severity.Error, FindingCodes.ModTomlMissing,
                $"'{modTomlPath}' is required and was not found.", modTomlPath));
            return null;
        }

        string text;
        try
        {
            text = archive.ReadAllText(modTomlEntry);
        }
        catch (InvalidDataException ex)
        {
            findings.Add(new Finding(4, Severity.Error, FindingCodes.ModTomlUnparseable,
                $"'{modTomlPath}' could not be read: {ex.Message}", modTomlPath));
            return null;
        }

        if (!ModToml.TryParse(text, out var toml, out var error) || toml is null)
        {
            findings.Add(new Finding(4, Severity.Error, FindingCodes.ModTomlUnparseable,
                $"'{modTomlPath}' is not valid TOML: {error}", modTomlPath));
            return null;
        }

        return (root, toml);
    }

    // ────────────────────────── Stage 5 and 6 ──────────────────────────

    private static (List<string> Declared, HashSet<string> Reachable, List<AssetIdRef> AssetIds)
        ValidateDeclarations(SafeArchive archive, string root, ModToml toml, List<Finding> findings)
    {
        var declared = new List<string>();
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assetIds = new List<AssetIdRef>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, relative) in toml.AllDeclaredPaths())
        {
            var full = CombineInRoot(root, relative);
            declared.Add(relative);

            if (!archive.TryGet(full, out var entry))
            {
                // The single most common way a content mod silently fails, and an error rather
                // than a warning because the loader will Path.Combine straight onto nothing.
                findings.Add(new Finding(5, Severity.Error, FindingCodes.DeclaredPathMissing,
                    $"mod.toml declares '{relative}' under '{key}', but no such file exists in the archive.",
                    relative));
                continue;
            }

            reachable.Add(full);

            // Hop two: XML declared under assets/systems/planetMeshes/... references further
            // files by path. Those are reachable and must not be reported as orphans.
            if (IsXmlKey(key))
            {
                ParseDeclaredXml(archive, root, entry, key, relative, findings, reachable, assetIds, seenIds);
            }
        }

        return (declared, reachable, assetIds);
    }

    private static void ParseDeclaredXml(
        SafeArchive archive, string root, ArchiveEntry entry, string key, string relative,
        List<Finding> findings, HashSet<string> reachable, List<AssetIdRef> assetIds, HashSet<string> seenIds)
    {
        XDocument? document;
        using (var stream = archive.Open(entry))
        {
            document = SafeXml.TryLoad(stream, out var error);
            if (document is null)
            {
                findings.Add(new Finding(5, Severity.Error, FindingCodes.DeclaredXmlUnparseable,
                    $"'{relative}' is not valid XML: {error}", relative));
                return;
            }
        }

        var rootName = document.Root?.Name.LocalName;
        var expectedRoots = ExpectedRootsFor(key);
        if (rootName is null || !expectedRoots.Contains(rootName, StringComparer.Ordinal))
        {
            findings.Add(new Finding(5, Severity.Error, FindingCodes.WrongXmlRoot,
                $"'{relative}' has root element <{rootName ?? "?"}>; '{key}' expects {string.Join(" or ", expectedRoots.Select(r => $"<{r}>"))}.",
                relative));
            // Still walk it: an author with the wrong root usually also wants the id list.
        }

        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                var value = attribute.Value;
                if (value.Length == 0) continue;

                // Stage 6: collect every Id attribute. Asset registration is a TryAdd into one
                // global table, so a duplicate from a later mod is silently discarded — this
                // index is the only way anyone finds out.
                if (attribute.Name.LocalName.Equals("Id", StringComparison.Ordinal))
                {
                    if (seenIds.Add(value))
                    {
                        assetIds.Add(new AssetIdRef(value, relative));
                    }
                    else
                    {
                        findings.Add(new Finding(6, Severity.Warning, FindingCodes.AssetIdDuplicateWithinMod,
                            $"Asset id '{value}' is declared more than once within this mod; only the first registration wins.",
                            relative));
                    }
                    continue;
                }

                if (!LooksLikePath(value)) continue;

                var full = CombineInRoot(root, value);
                if (archive.TryGet(full, out _))
                {
                    reachable.Add(full);
                }
                else
                {
                    findings.Add(new Finding(5, Severity.Warning, FindingCodes.ReferencedPathMissing,
                        $"'{relative}' references '{value}', which is not in the archive.", relative));
                }
            }
        }
    }

    /// <summary>
    /// Reports content present in the archive but reachable from neither mod.toml nor any
    /// declared XML.
    ///
    /// <para>Reachability rather than declaration is the whole point (spec §7). Textures, meshes
    /// and shaders are correctly absent from mod.toml — they are referenced from inside the XML
    /// that <i>is</i> declared. Warning on everything not named in mod.toml would fire on the
    /// normal shape of every content mod, and the warning authors ignore is worth less than no
    /// warning at all.</para>
    /// </summary>
    private static void ReportUnreachable(
        SafeArchive archive, string root, HashSet<string> reachable, List<Finding> findings)
    {
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory) continue;
            if (reachable.Contains(entry.Path)) continue;

            var name = entry.FileName;

            // Documentation, licences and the descriptor itself are not content.
            if (name.Equals("mod.toml", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsDocumentation(name)) continue;
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var artefact in BuildArtefactExtensions)
            {
                if (name.EndsWith(artefact, StringComparison.OrdinalIgnoreCase)) goto next;
            }

            if (!ContentExtensions.Any(entry.HasExtension)) continue;

            var relative = entry.Path[(root.Length + 1)..];
            findings.Add(new Finding(5, Severity.Warning, FindingCodes.UnreachableContentFile,
                $"'{relative}' is not declared in mod.toml and is not referenced from any declared XML, so the game will never load it.",
                relative));

        next: ;
        }
    }

    // ────────────────────────── Stage 7 ──────────────────────────

    private static List<ShippedAssembly> InspectAssemblies(
        SafeArchive archive, string root, string expectedId, ModToml toml, List<Finding> findings)
    {
        var assemblies = new List<ShippedAssembly>();
        var dlls = archive.Entries
            .Where(e => !e.IsDirectory && e.HasExtension(".dll"))
            .ToList();

        foreach (var entry in dlls)
        {
            var relative = entry.Path[(root.Length + 1)..];
            var name = entry.FileName;

            if (IsGameAssembly(name))
            {
                findings.Add(new Finding(7, Severity.Error, FindingCodes.GameAssemblyShipped,
                    $"'{relative}' is a game or engine assembly. These are compile-time references the game already loads; " +
                    "mark the reference Private=\"false\" so it is not copied to output.",
                    relative));
                continue;
            }

            if (IsLoaderAssembly(name))
            {
                findings.Add(new Finding(7, Severity.Warning, FindingCodes.LoaderAssemblyShipped,
                    $"'{relative}' is supplied by the loader at runtime. Reference the StarMap.API NuGet package with " +
                    "Private=\"false\" rather than shipping it.",
                    relative));
            }

            // Metadata-only inspection. PEReader/MetadataReader never runs a static constructor
            // or a module initialiser — it is the layer MetadataLoadContext is built on, and it
            // needs no resolver and no file path, so it is the stricter choice here (§14.4).
            string? assemblyName = null;
            string? assemblyVersion = null;
            try
            {
                using var stream = archive.Open(entry);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                buffer.Position = 0;

                using var pe = new PEReader(buffer);
                if (pe.HasMetadata)
                {
                    var reader = pe.GetMetadataReader();
                    var definition = reader.GetAssemblyDefinition();
                    assemblyName = reader.GetString(definition.Name);
                    assemblyVersion = definition.Version.ToString();
                }
            }
            catch (BadImageFormatException)
            {
                findings.Add(new Finding(7, Severity.Warning, FindingCodes.AssemblyUnreadable,
                    $"'{relative}' has a .dll extension but is not a readable .NET assembly.", relative));
            }
            catch (InvalidDataException)
            {
                findings.Add(new Finding(7, Severity.Warning, FindingCodes.AssemblyUnreadable,
                    $"'{relative}' could not be read from the archive.", relative));
            }

            assemblies.Add(new ShippedAssembly
            {
                FilePath = relative,
                AssemblyName = assemblyName,
                AssemblyVersion = assemblyVersion,
            });
        }

        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            foreach (var artefact in BuildArtefactExtensions)
            {
                if (!entry.HasExtension(artefact)) continue;
                findings.Add(new Finding(7, Severity.Info, FindingCodes.BuildArtefactShipped,
                    $"'{entry.Path[(root.Length + 1)..]}' is a build artefact. Harmless, but strip it for release builds.",
                    entry.Path[(root.Length + 1)..]));
            }
        }

        if (assemblies.Count == 0) return assemblies;

        // The entry assembly is [StarMap].EntryAssembly when set, otherwise the mod id — that
        // default is why AircraftHUD loads with no [StarMap] block at all.
        var entryName = toml.EntryAssembly ?? expectedId;
        var expectedPath = $"{root}/{entryName}.dll";

        if (archive.TryGet(expectedPath, out _))
        {
            for (var i = 0; i < assemblies.Count; i++)
            {
                if (string.Equals(assemblies[i].FilePath, $"{entryName}.dll", StringComparison.OrdinalIgnoreCase))
                {
                    assemblies[i] = assemblies[i] with { IsEntry = true };
                }
            }
        }
        else if (toml.EntryAssembly is not null)
        {
            findings.Add(new Finding(7, Severity.Error, FindingCodes.EntryAssemblyMissing,
                $"[StarMap] declares EntryAssembly = \"{entryName}\", but '{entryName}.dll' is not at the mod root."));
        }
        else
        {
            findings.Add(new Finding(7, Severity.Warning, FindingCodes.EntryAssemblyMissing,
                $"This mod ships assemblies but has no [StarMap] section, so the loader will look for '{entryName}.dll' " +
                "at the mod root and find nothing. Add an EntryAssembly key or rename the assembly."));
        }

        return assemblies;
    }

    // ────────────────────────── Stage 8 ──────────────────────────

    private static List<ConsoleCommand> CollectConsole(ModToml toml, List<Finding> findings)
    {
        var commands = new List<ConsoleCommand>();

        for (var i = 0; i < toml.ConsoleOnBoot.Count; i++)
        {
            commands.Add(new ConsoleCommand("onBoot", i, toml.ConsoleOnBoot[i]));
        }
        for (var i = 0; i < toml.ConsoleOnLoad.Count; i++)
        {
            commands.Add(new ConsoleCommand("onLoad", i, toml.ConsoleOnLoad[i]));
        }

        if (commands.Count > 0)
        {
            // A legitimate feature — Core uses it for the opening camera angle — and also the
            // most direct scripted-behaviour vector in a mod.toml. Displayed verbatim, never
            // silently stripped.
            findings.Add(new Finding(8, Severity.Info, FindingCodes.ConsoleBlockPresent,
                $"This mod runs {commands.Count} developer console command(s) automatically. They are shown in full on the listing."));
        }

        return commands;
    }

    private static void ReportCoreOverrides(
        List<AssetIdRef> assetIds, IReadOnlySet<string>? coreIds, List<Finding> findings)
    {
        if (coreIds is null || coreIds.Count == 0) return;

        foreach (var asset in assetIds)
        {
            if (!coreIds.Contains(asset.Id)) continue;

            // Overriding stock content requires the mod to load before Core, which means editing
            // manifest.toml order — a file the game rewrites freely. A fragile category, and
            // users deserve to know before installing.
            findings.Add(new Finding(8, Severity.Warning, FindingCodes.CoreIdOverride,
                $"Asset id '{asset.Id}' also exists in Core, so this mod overrides stock content. " +
                "That requires load-order placement before Core and is fragile.",
                asset.XmlPath));
        }
    }

    // ────────────────────────── helpers ──────────────────────────

    private static bool IsXmlKey(string key) =>
        key is "assets" or "systems" or "planetMeshes" or "planetRandomHeightmapCollections";

    private static string[] ExpectedRootsFor(string key) => key switch
    {
        "assets" => ["Assets"],
        "systems" => ["System"],
        // The collection roots are not readable from KSA.dll; accept the documented shapes
        // rather than guessing a single one.
        "planetMeshes" => ["MeshCollection", "MeshCollections", "Assets"],
        "planetRandomHeightmapCollections" => ["TextureCollection", "TextureCollections", "Assets"],
        _ => ["Assets"],
    };

    private static bool IsDocumentation(string name) =>
        name.StartsWith("README", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("LICENCE", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase);

    private static bool IsGameAssembly(string fileName)
    {
        foreach (var exact in GameAssemblyExact)
        {
            if (string.Equals(fileName, exact, StringComparison.OrdinalIgnoreCase)) return true;
        }
        foreach (var prefix in GameAssemblyPrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsLoaderAssembly(string fileName)
    {
        foreach (var exact in LoaderAssemblyExact)
        {
            if (string.Equals(fileName, exact, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool LooksLikePath(string value)
    {
        if (value.Length is 0 or > 512) return false;
        if (value.Contains("://", StringComparison.Ordinal)) return false;
        foreach (var ext in ContentExtensions)
        {
            if (value.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves a mod-relative path against the root, matching what the loader does when it
    /// Path.Combines a declared path onto the mod folder. Returns a path that can never escape
    /// the root, so a hostile mod.toml cannot make the validator read outside the archive.
    /// </summary>
    private static string CombineInRoot(string root, string relative)
    {
        var cleaned = relative.Replace('\\', '/').TrimStart('/');
        var segments = new List<string>();

        foreach (var segment in cleaned.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }

        return segments.Count == 0 ? root : $"{root}/{string.Join('/', segments)}";
    }
}
