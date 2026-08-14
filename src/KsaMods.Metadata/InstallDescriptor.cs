using System.Text.Json.Serialization;

namespace KsaMods.Metadata;

/// <summary>
/// Where content is written (RFC 0035). An anchor the manager resolves at install time, never an
/// absolute path: the format cannot name a location on somebody else's disk.
/// </summary>
public static class InstallAnchor
{
    /// <summary>The game's mods folder. An anchor rather than user-data plus a path, because an
    /// instance override moves it with the profile.</summary>
    public const string Mods = "mods";

    /// <summary>The game's user data root, where saves, vehicles and the manifest live.</summary>
    public const string UserData = "user-data";

    /// <summary>The directory holding the game executable and <c>Content/</c>.</summary>
    public const string GameRoot = "game-root";

    /// <summary>A directory of the manager's own choosing, outside all three.</summary>
    public const string Standalone = "standalone";

    /// <summary>
    /// Closed by design. An unrecognised anchor is rejected rather than ignored: a future value
    /// means a layout this manager cannot perform, and guessing writes files somewhere the author
    /// did not choose.
    /// </summary>
    public static bool IsKnown(string value) =>
        value is Mods or UserData or GameRoot or Standalone;
}

/// <summary>
/// What a loader offers the content installed under it (RFC 0035). A different question from where
/// the loader itself goes, which is why it is a different section.
///
/// <para>Deliberately not stamped into a release file: it says what the loader offers <i>now</i>,
/// and a manager acting on a stale copy would write mods into a directory the installed loader no
/// longer reads.</para>
/// </summary>
public sealed record ProvidesBlock
{
    /// <summary>
    /// The executable the player runs after installing, relative to the loader's install location
    /// and run with the working directory set there (rule 5). Absent means installing this loader
    /// does not change what you launch.
    /// </summary>
    [JsonPropertyName("launch")] public string? Launch { get; init; }

    /// <summary>
    /// The anchor this loader reads content from. Absent means it reads no content directory, and
    /// a manager must not invent one.
    /// </summary>
    [JsonPropertyName("content-dir")] public string? ContentDir { get; init; }

    [JsonPropertyName("content-path")] public string? ContentPath { get; init; }

    [JsonPropertyName("configure")] public ConfigureBlock? Configure { get; init; }
}

/// <summary>
/// The loader's own configuration file, and which well-known value a manager writes where.
///
/// <para>The value side is a closed enum of exactly one member in <c>spec_version = 1</c>. That is
/// the whole thing keeping this from becoming a configuration language: the author supplies an
/// address, the manager supplies a fact it already holds, and nothing here is a template.</para>
/// </summary>
public sealed record ConfigureBlock
{
    /// <summary>Path to the configuration file, relative to the loader's install location.</summary>
    [JsonPropertyName("file")] public required string File { get; init; }

    /// <summary>json or toml. A manager must be able to write it without a parser it lacks.</summary>
    [JsonPropertyName("format")] public required string Format { get; init; }

    /// <summary>
    /// The key that receives the absolute path of the game directory, addressed by dot-separated
    /// path from the document root. A key whose own name contains a dot is unaddressable here.
    /// </summary>
    [JsonPropertyName("game-path")] public string? GamePath { get; init; }
}

/// <summary>
/// RFC 0035's validity rules, checked in one place.
///
/// <para><b>Path containment is a validity rule, not a recommendation.</b> An install descriptor is
/// executed by a manager holding write access to a game directory, so a path that escapes its
/// anchor is the difference between installing a mod and writing wherever the author pleased.</para>
/// </summary>
public static class InstallDescriptor
{
    public const string FormatJson = "json";
    public const string FormatToml = "toml";

    /// <summary>
    /// The game's own file. <c>ModManifest.Save</c> rewrites it from the game's in-memory list and
    /// destroys any key, comment or formatting it did not write, so no descriptor may claim it and
    /// a manager must expect it to change underneath both of them (rule 6).
    /// </summary>
    public const string GameOwnedManifest = "manifest.toml";

    /// <summary>
    /// Checks a descriptor. Returns null when it is valid, or the reason it is not, phrased for
    /// the author who has to fix it.
    /// </summary>
    public static string? Check(string type, InstallBlock? install, ProvidesBlock? provides)
    {
        if (install is not null && type == ContentType.ModPack)
        {
            return "[install] is not valid on a modpack: a pack ships no files and installs nothing of its own.";
        }

        if (provides is not null && type != ContentType.ModLoader)
        {
            return $"[provides] is only valid on a mod-loader, not on '{type}'.";
        }

        if (install is not null)
        {
            if (CheckPath(install.Root, "install.root") is { } rootError) return rootError;
            if (CheckPath(install.Path, "install.path") is { } pathError) return pathError;

            if (install.Target is { } target)
            {
                if (!InstallAnchor.IsKnown(target))
                {
                    return $"install.target '{target}' is not an anchor this format knows.";
                }

                // Rule 4. A directory nothing ever runs from and nothing reads is not an install,
                // and permitting it would let a descriptor scatter files with no way to reach them.
                if (target == InstallAnchor.Standalone && provides?.Launch is null or "")
                {
                    return "install.target = \"standalone\" needs [provides].launch: "
                         + "nothing would ever run from the directory otherwise.";
                }
            }
            else if (!HasDefaultTarget(type))
            {
                // A mod-loader has no convention to fall back on. Rather than guess, a manager
                // installs nothing and shows the listing's links.
                return $"install.target is required on a {type}: there is no default install location for it.";
            }

            foreach (var managed in install.Manages ?? [])
            {
                if (CheckPath(managed, "install.manages") is { } managedError) return managedError;

                if (install.Target == InstallAnchor.UserData &&
                    Normalise(managed).Equals(GameOwnedManifest, StringComparison.OrdinalIgnoreCase))
                {
                    return $"install.manages cannot claim {GameOwnedManifest} under user-data: it is the game's own file.";
                }
            }
        }

        if (provides is null) return null;

        if (CheckPath(provides.Launch, "provides.launch") is { } launchError) return launchError;
        if (CheckPath(provides.ContentPath, "provides.content-path") is { } contentError) return contentError;

        if (provides.ContentDir is { } contentDir && !InstallAnchor.IsKnown(contentDir))
        {
            return $"provides.content-dir '{contentDir}' is not an anchor this format knows.";
        }

        if (provides.Configure is { } configure)
        {
            if (CheckPath(configure.File, "provides.configure.file") is { } fileError) return fileError;

            if (string.IsNullOrWhiteSpace(configure.File))
            {
                return "provides.configure.file is required.";
            }

            if (configure.Format is not (FormatJson or FormatToml))
            {
                return $"provides.configure.format '{configure.Format}' is not one a manager can write.";
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an absent <c>target</c> has a meaning for this type.
    ///
    /// <para>Vehicles and saves are deliberately absent. RFC 0035 gives them a default of
    /// <c>user-data</c> plus the path the game uses, but leaves that path to those types' own RFCs,
    /// which do not exist. Treating them as having a default would mean inventing a folder name.</para>
    /// </summary>
    private static bool HasDefaultTarget(string type) => type == ContentType.Mod;

    /// <summary>
    /// Rules 1 and 2: relative, <c>/</c>-separated, and it must not leave where it started.
    ///
    /// <para>Resolved by walking segments rather than by string matching, because <c>a/../../b</c>
    /// escapes while <c>a/../b</c> does not, and only counting tells them apart.</para>
    /// </summary>
    private static string? CheckPath(string? path, string field)
    {
        if (path is null) return null;
        if (path.Length == 0) return null;

        if (path.Contains('\\', StringComparison.Ordinal))
        {
            return $"{field} must use / as its separator, on every platform.";
        }

        if (path.StartsWith('/') || path.StartsWith('~'))
        {
            return $"{field} must be relative: '{path}' names a location outside the install.";
        }

        // A Windows drive letter is absolute too, and it does not start with a slash.
        if (path.Length >= 2 && path[1] == ':')
        {
            return $"{field} must be relative: '{path}' names a location outside the install.";
        }

        var depth = 0;

        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or ".") continue;

            if (segment == "..")
            {
                if (--depth < 0)
                {
                    return $"{field} escapes its anchor: '{path}' resolves outside the install location.";
                }

                continue;
            }

            depth++;
        }

        return null;
    }

    private static string Normalise(string path) =>
        string.Join('/', path.Split('/').Where(s => s is not ("" or ".")));
}
