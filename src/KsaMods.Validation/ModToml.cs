using Tomlyn;
using Tomlyn.Model;

namespace KsaMods.Validation;

/// <summary>
/// The archive's own <c>mod.toml</c>, read as a data source and never written to.
///
/// <para>Only the keys this project needs are modelled. The game reads 17 keys and ignores
/// everything else, which is why community keys and the loader's own section can coexist with
/// engine keys in one file.</para>
/// </summary>
public sealed class ModToml
{
    /// <summary>
    /// The six keys whose values are loaded directly by path. <b>Everything else a mod ships is
    /// referenced from inside these files' XML</b>, which is why reachability is two hops
    /// (spec §7) and why warning on "not in mod.toml" fires on every correct content mod.
    /// </summary>
    public static readonly string[] PathListKeys =
    [
        "assets",
        "systems",
        "fonts",
        "starBinaries",
        "planetMeshes",
        "planetRandomHeightmapCollections",
    ];

    public string? Name { get; private init; }

    /// <summary>Declared paths by key, relative to the mod folder.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DeclaredPaths { get; private init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// <c>[StarMap].EntryAssembly</c>, without the <c>.dll</c>. Null when the section is absent -
    /// in which case the loader defaults it to the mod id, which is why AircraftHUD loads with no
    /// block at all (spec §5.2).
    /// </summary>
    public string? EntryAssembly { get; private init; }

    public IReadOnlyList<string> ExportedAssemblies { get; private init; } = [];

    /// <summary>
    /// <c>[[StarMap.ModDependencies]]</c> - the only machine-readable dependency data that exists
    /// anywhere in the KSA ecosystem, and ground truth because the loader acts on it at runtime.
    /// </summary>
    public IReadOnlyList<StarMapDependency> ModDependencies { get; private init; } = [];

    /// <summary><c>[console]</c> onBoot commands. Arbitrary developer-console execution (spec §9).</summary>
    public IReadOnlyList<string> ConsoleOnBoot { get; private init; } = [];
    public IReadOnlyList<string> ConsoleOnLoad { get; private init; } = [];

    /// <summary>Advisory community keys. The index record is authoritative (spec §5.3).</summary>
    public string? CommunityVersion { get; private init; }
    public string? CommunityAuthor { get; private init; }

    public bool HasStarMapSection { get; private init; }

    public static bool TryParse(string text, out ModToml? result, out string? error)
    {
        result = null;
        error = null;

        if (!Toml.TryToModel<TomlTable>(text, out var table, out var diagnostics) || table is null)
        {
            error = diagnostics?.FirstOrDefault()?.Message ?? "The file is not valid TOML.";
            return false;
        }

        var declared = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var key in PathListKeys)
        {
            var paths = ReadStringList(table, key);
            if (paths.Count > 0) declared[key] = paths;
        }

        string? entryAssembly = null;
        var exported = new List<string>();
        var dependencies = new List<StarMapDependency>();
        var hasStarMap = false;

        if (table.TryGetValue("StarMap", out var starMapValue) && starMapValue is TomlTable starMap)
        {
            hasStarMap = true;
            entryAssembly = ReadString(starMap, "EntryAssembly");
            exported.AddRange(ReadStringList(starMap, "ExportedAssemblies"));

            if (starMap.TryGetValue("ModDependencies", out var depsValue) && depsValue is TomlTableArray deps)
            {
                foreach (var dep in deps)
                {
                    var modId = ReadString(dep, "ModId");
                    if (string.IsNullOrWhiteSpace(modId)) continue;

                    dependencies.Add(new StarMapDependency
                    {
                        ModId = modId,
                        // Default false: the loader refuses to start a mod whose non-optional
                        // dependency is missing, so absent means required.
                        Optional = ReadBool(dep, "Optional") ?? false,
                        ImportedAssemblies = ReadStringList(dep, "ImportedAssemblies"),
                    });
                }
            }
        }

        var onBoot = new List<string>();
        var onLoad = new List<string>();
        if (table.TryGetValue("console", out var consoleValue) && consoleValue is TomlTable console)
        {
            onBoot.AddRange(ReadStringList(console, "onBoot"));
            onLoad.AddRange(ReadStringList(console, "onLoad"));
        }

        result = new ModToml
        {
            Name = ReadString(table, "name"),
            DeclaredPaths = declared,
            EntryAssembly = entryAssembly,
            ExportedAssemblies = exported,
            ModDependencies = dependencies,
            ConsoleOnBoot = onBoot,
            ConsoleOnLoad = onLoad,
            CommunityVersion = ReadString(table, "version"),
            CommunityAuthor = ReadString(table, "author"),
            HasStarMapSection = hasStarMap,
        };
        return true;
    }

    /// <summary>Every declared path across all six keys, in declaration order.</summary>
    public IEnumerable<(string Key, string Path)> AllDeclaredPaths()
    {
        foreach (var key in PathListKeys)
        {
            if (!DeclaredPaths.TryGetValue(key, out var paths)) continue;
            foreach (var path in paths) yield return (key, path);
        }
    }

    private static string? ReadString(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is string s ? s : null;

    private static bool? ReadBool(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is bool b ? b : null;

    private static IReadOnlyList<string> ReadStringList(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlArray array) return [];

        var items = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is string s && !string.IsNullOrWhiteSpace(s)) items.Add(s);
        }
        return items;
    }
}

public sealed record StarMapDependency
{
    /// <summary>Matched against the mod id, which is the folder name.</summary>
    public required string ModId { get; init; }
    public required bool Optional { get; init; }
    public IReadOnlyList<string> ImportedAssemblies { get; init; } = [];
}
