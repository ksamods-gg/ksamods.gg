using System.IO.Compression;
using System.Text;

namespace KsaMods.Tests.Fixtures;

/// <summary>
/// Builds mod archives in memory.
///
/// <para>backend.md §17 asks for a fixture corpus with one deliberately broken archive per
/// pipeline stage. Building them in code rather than committing binaries keeps the hostile
/// fixtures — zip bombs, traversal entries — reviewable as source instead of opaque blobs, and
/// means a reader can see exactly what makes each one hostile.</para>
/// </summary>
public sealed class ArchiveBuilder
{
    private readonly List<(string Path, byte[] Content, int? ExternalAttributes)> _entries = [];

    public static ArchiveBuilder New() => new();

    public ArchiveBuilder WithFile(string path, string content) =>
        WithFile(path, Encoding.UTF8.GetBytes(content));

    public ArchiveBuilder WithFile(string path, byte[] content)
    {
        _entries.Add((path, content, null));
        return this;
    }

    /// <summary>
    /// Adds an entry whose unix mode marks it a symlink (S_IFLNK), the way a zip produced on
    /// Linux or macOS stores one: st_mode in the high 16 bits of the external attributes.
    /// </summary>
    public ArchiveBuilder WithSymlink(string path, string target)
    {
        const int SIfLnk = 0xA000;
        const int Permissions = 0x1FF;
        var attributes = unchecked((SIfLnk | Permissions) << 16);
        _entries.Add((path, Encoding.UTF8.GetBytes(target), attributes));
        return this;
    }

    /// <summary>A large run of one byte: compresses to almost nothing, expands enormously.</summary>
    public ArchiveBuilder WithHighlyCompressibleFile(string path, int sizeBytes)
    {
        _entries.Add((path, new byte[sizeBytes], null));
        return this;
    }

    public MemoryStream Build()
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content, attributes) in _entries)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                if (attributes is not null) entry.ExternalAttributes = attributes.Value;

                using var stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }
        buffer.Position = 0;
        return buffer;
    }
}

/// <summary>Ready-made archives covering the shapes described in spec §14.</summary>
public static class ModArchives
{
    public const string CodeModId = "AdvancedFlightComputer";
    public const string ContentModId = "OuterPlanets";

    /// <summary>A well-formed code mod: entry assembly plus a private dependency at the root.</summary>
    public static MemoryStream CodeMod() => ArchiveBuilder.New()
        .WithFile($"{CodeModId}/mod.toml", """
            name = "Advanced Flight Computer"
            version = "1.2.0"
            author = "Maxi"

            [StarMap]
            EntryAssembly = "AdvancedFlightComputer"
            """)
        .WithFile($"{CodeModId}/README.md", "# Advanced Flight Computer")
        .WithFile($"{CodeModId}/LICENSE.txt", "MIT")
        .WithFile($"{CodeModId}/AdvancedFlightComputer.dll", FakeAssembly())
        .WithFile($"{CodeModId}/Newtonsoft.Json.dll", FakeAssembly())
        .Build();

    /// <summary>
    /// A well-formed content mod. Note the texture is deliberately <b>not</b> declared in
    /// mod.toml — it is referenced from inside the declared XML, which is the normal and correct
    /// shape (spec §14) and the reason reachability is two hops rather than one.
    /// </summary>
    public static MemoryStream ContentMod() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", """
            name = "Outer Planets"

            assets = [ "Assets/OuterPlanetsBodies.xml" ]
            systems = [ "Systems/OuterPlanets.xml" ]
            """)
        .WithFile($"{ContentModId}/Assets/OuterPlanetsBodies.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <Assets>
              <Planet Id="OuterPlanets_Persephone" Diffuse="Textures/Persephone_Diffuse.ktx2" />
            </Assets>
            """)
        .WithFile($"{ContentModId}/Systems/OuterPlanets.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <System>
              <Body Id="OuterPlanets_PersephoneBody" />
            </System>
            """)
        .WithFile($"{ContentModId}/Textures/Persephone_Diffuse.ktx2", new byte[64])
        .Build();

    /// <summary>mod.toml declares a file that is not in the archive. Stage 5 error.</summary>
    public static MemoryStream MissingDeclaredPath() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", """
            name = "Outer Planets"
            assets = [ "Assets/DoesNotExist.xml" ]
            """)
        .Build();

    /// <summary>A declared XML file that does not parse. Stage 5 error.</summary>
    public static MemoryStream MalformedXml() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", """
            name = "Outer Planets"
            assets = [ "Assets/Broken.xml" ]
            """)
        .WithFile($"{ContentModId}/Assets/Broken.xml", "<Assets><Planet Id=\"x\"></Assets>")
        .Build();

    /// <summary>A declared XML file whose root element is wrong for its key. Stage 5 error.</summary>
    public static MemoryStream WrongXmlRoot() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", """
            name = "Outer Planets"
            systems = [ "Systems/NotASystem.xml" ]
            """)
        .WithFile($"{ContentModId}/Systems/NotASystem.xml", "<Assets><Planet Id=\"x\" /></Assets>")
        .Build();

    /// <summary>A texture reachable from nothing. Stage 5 warning — the genuinely-orphaned case.</summary>
    public static MemoryStream OrphanedContent() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", """
            name = "Outer Planets"
            assets = [ "Assets/Bodies.xml" ]
            """)
        .WithFile($"{ContentModId}/Assets/Bodies.xml", "<Assets><Planet Id=\"OuterPlanets_A\" /></Assets>")
        .WithFile($"{ContentModId}/Textures/NobodyReferencesThis.ktx2", new byte[32])
        .Build();

    /// <summary>Root directory name does not match the claimed id. Stage 4 error, fatal.</summary>
    public static MemoryStream WrongRootName() => ArchiveBuilder.New()
        .WithFile("SomethingElse/mod.toml", "name = \"x\"")
        .Build();

    /// <summary>Two top-level directories. Stage 4 error.</summary>
    public static MemoryStream TwoRoots() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", "name = \"x\"")
        .WithFile("Extra/readme.txt", "hello")
        .Build();

    /// <summary>Ships the game's own assembly. Stage 7 error.</summary>
    public static MemoryStream ShipsGameAssembly() => ArchiveBuilder.New()
        .WithFile($"{CodeModId}/mod.toml", """
            name = "x"
            [StarMap]
            EntryAssembly = "AdvancedFlightComputer"
            """)
        .WithFile($"{CodeModId}/AdvancedFlightComputer.dll", FakeAssembly())
        .WithFile($"{CodeModId}/KSA.dll", FakeAssembly())
        .Build();

    /// <summary>Ships the loader's API assembly. Stage 7 warning, not an error.</summary>
    public static MemoryStream ShipsLoaderAssembly() => ArchiveBuilder.New()
        .WithFile($"{CodeModId}/mod.toml", """
            name = "x"
            [StarMap]
            EntryAssembly = "AdvancedFlightComputer"
            """)
        .WithFile($"{CodeModId}/AdvancedFlightComputer.dll", FakeAssembly())
        .WithFile($"{CodeModId}/StarMap.API.dll", FakeAssembly())
        .Build();

    /// <summary>No [StarMap] section but a DLL named after the mod id — the AircraftHUD shape.</summary>
    public static MemoryStream NoStarMapSectionButEntryDllPresent() => ArchiveBuilder.New()
        .WithFile($"{CodeModId}/mod.toml", "name = \"x\"")
        .WithFile($"{CodeModId}/{CodeModId}.dll", FakeAssembly())
        .Build();

    /// <summary>Declares [StarMap].EntryAssembly but ships no such DLL. Stage 7 error.</summary>
    public static MemoryStream EntryAssemblyMissing() => ArchiveBuilder.New()
        .WithFile($"{CodeModId}/mod.toml", """
            name = "x"
            [StarMap]
            EntryAssembly = "NotShipped"
            """)
        .WithFile($"{CodeModId}/Something.dll", FakeAssembly())
        .Build();

    /// <summary>Carries a [console] block. Stage 8 info, recorded and displayed verbatim.</summary>
    public static MemoryStream WithConsoleBlock() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", """
            name = "x"

            [console]
            onBoot = [ "noclip" ]
            onLoad = [ "simspeed 1", "camera map 2.88 0.47 3635076" ]
            """)
        .Build();

    /// <summary>Declares StarMap dependencies. Stage 7b extraction.</summary>
    public static MemoryStream WithModDependencies() => ArchiveBuilder.New()
        .WithFile($"{CodeModId}/mod.toml", """
            name = "x"

            [StarMap]
            EntryAssembly = "AdvancedFlightComputer"

            [[StarMap.ModDependencies]]
            ModId = "KittenExtensions"
            Optional = true
            ImportedAssemblies = [ "KittenExtensions" ]

            [[StarMap.ModDependencies]]
            ModId = "CoreLib"
            """)
        .WithFile($"{CodeModId}/AdvancedFlightComputer.dll", FakeAssembly())
        .Build();

    // ── hostile fixtures ──

    /// <summary>Path traversal escaping the archive root. Stage 3, fatal.</summary>
    public static MemoryStream PathTraversal() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", "name = \"x\"")
        .WithFile("../../etc/passwd", "root:x:0:0")
        .Build();

    /// <summary>A symlink entry. Stage 3, fatal.</summary>
    public static MemoryStream ContainsSymlink() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", "name = \"x\"")
        .WithSymlink($"{ContentModId}/escape", "/etc/passwd")
        .Build();

    /// <summary>A nested archive. Stage 3, fatal.</summary>
    public static MemoryStream NestedArchive() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", "name = \"x\"")
        .WithFile($"{ContentModId}/payload.zip", new byte[128])
        .Build();

    /// <summary>Zip bomb: 64 MB of zeroes compresses to a few kilobytes.</summary>
    public static MemoryStream ZipBomb() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", "name = \"x\"")
        .WithHighlyCompressibleFile($"{ContentModId}/bomb.bin", 64 * 1024 * 1024)
        .Build();

    /// <summary>Carries the game's manifest.toml, which never belongs inside a mod (spec §8).</summary>
    public static MemoryStream ContainsReservedFile() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", "name = \"x\"")
        .WithFile($"{ContentModId}/manifest.toml", "[[mods]]\nid = \"x\"")
        .Build();

    /// <summary>An XXE attempt inside declared content. Must be neutralised, not fetched.</summary>
    public static MemoryStream XxeAttempt() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", """
            name = "x"
            assets = [ "Assets/Evil.xml" ]
            """)
        .WithFile($"{ContentModId}/Assets/Evil.xml", """
            <?xml version="1.0"?>
            <!DOCTYPE Assets [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <Assets><Planet Id="&xxe;" /></Assets>
            """)
        .Build();

    /// <summary>macOS archiver droppings, which are stripped rather than rejected.</summary>
    public static MemoryStream WithJunkFiles() => ArchiveBuilder.New()
        .WithFile($"{ContentModId}/mod.toml", "name = \"x\"")
        .WithFile("__MACOSX/._mod.toml", new byte[16])
        .WithFile($"{ContentModId}/.DS_Store", new byte[16])
        .Build();

    /// <summary>
    /// A minimal but genuine PE/COFF .NET assembly, so stage 7 exercises real metadata reading
    /// rather than a stub path. This is the compiled form of an empty library.
    /// </summary>
    private static byte[] FakeAssembly()
    {
        // Emitting a real assembly at runtime needs Roslyn; instead reuse one already on disk.
        // KsaMods.Metadata.dll is a real, small, managed assembly built alongside these tests.
        var path = typeof(Metadata.ContentId).Assembly.Location;
        return File.ReadAllBytes(path);
    }
}
