# KSA Mod Archive (.zip) Structure Standard

**Status:** Draft v0.2
**Companion to:** KSA Mod Registry Specification
**Verified against:** KSA build `2026.8.5.5168`, StarMap `0.4.6`
**Scope:** the internal layout of a distributable KSA mod archive, what a registry must validate on upload,
and how a client must install, update and remove one.

---

## 0. Grounding

Every rule is tagged:

- **[CONFIRMED]** read from decompiled `KSA.dll` for build 2026.8.5.5168, or observed in published mods.
- **[CONVENTION]** a choice this standard makes. The loaders neither require nor forbid it.
- **[@VERIFY]** depends on StarMap internals not readable from `KSA.dll`. Confirm with the StarMap authors.

Nothing below is inferred from folder names or naming similarity. Where a claim could not be checked it is
marked `[@VERIFY]` rather than guessed.

---

## 1. How KSA actually loads a mod

A packaging standard that does not match this will produce archives that install and then do nothing. Read
this section before the rules.

**[CONFIRMED] Mod identity is the folder name.** There is no id field anywhere. The folder name is the key in
`manifest.toml`, the key StarMap uses, and the namespace for everything the mod registers.

**[CONFIRMED] Two locations, `Content` wins.** For each enabled entry the game looks for
`<GameDir>/Content/<Id>/mod.toml` first, then `<Documents>/My Games/Kitten Space Agency/mods/<Id>/mod.toml`.
Third-party mods belong in the second.

**[CONFIRMED] Discovery is one level deep.** `ModLibrary.AddMods` lists the immediate subdirectories of each
location and keeps the ones containing a `mod.toml`. Nested mods are not found.

**[CONFIRMED] New mods are disabled by default.** A freshly discovered folder is added to `manifest.toml` with
`enabled = false`. Installing a mod is not enough to activate it.

**[CONFIRMED] Content is declared, never discovered.** `mod.toml` carries explicit relative-path lists.
`LoadAssetBundles`, `LoadSystems`, `LoadFonts`, `LoadStarBinaries`, `LoadPlanetMeshes` and
`LoadPlanetRandomHeightmaps` each iterate their list and `Path.Combine` it against the mod folder. **No part of
the loader walks the tree looking for content.** A file not listed in `mod.toml` is inert no matter where it
sits.

**[CONFIRMED] Content is XML.** Three root elements exist: `<Assets>`, `<System>`, `<VehicleSaveData>`. JSON is
used nowhere in content loading.

**[CONFIRMED] Load order is `manifest.toml` order, and ids are first-wins.** Asset registration is a `TryAdd`
into one global table, so a duplicate id from a later mod is silently discarded with no error. Order in
`manifest.toml` decides who wins.

**[CONFIRMED] The game rewrites `manifest.toml`.** Toggling mods in-game regenerates the file with only `id`
and `enabled` per entry. A manager cannot store anything else in it, and formatting is not preserved.

**[CONFIRMED] KSA has no concept of a mod version.** No version key, no compatibility field, no dependency
mechanism. All of that lives in the registry.

---

## 2. The archive is the mod folder

**[CONVENTION]** The archive root must contain **exactly one top-level directory**, named with the mod `Id`.
Everything else lives inside it.

```
author.coolmod-1.2.0.zip
└── author.coolmod/          <- single root dir, equals Id, equals manifest key
    ├── mod.toml
    └── ...
```

Installing is then one move into `mods/`, with no inference and no way for a malformed archive to scatter
files across the mods directory.

**Rejected:** rooting files at the archive top. The installer would have to derive the folder name from
`mod.toml` `name`, which mods are free to set to anything, including something that does not match.

---

## 3. Id rules

**[CONFIRMED] The Id is the folder name, and it is the global namespace.** Registration hashes the string, so
`MyMod` and `mymod` are different mods to the lookup, while the filesystem probe that finds the folder is
case-insensitive on Windows and case-sensitive on Linux. Treat the Id as case-sensitive and exact.

**[CONVENTION] Registry Id format:** `<author>.<mod>`, lowercase, `[a-z0-9]` segments separated by `.`, `-` or
`_`, 3 to 64 characters. Reject anything that is not a valid directory name on Windows, macOS and Linux, and
reject the reserved Windows device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).

**[CONVENTION] `Core` is reserved.** It is the game's own mod.

**[CONVENTION] Asset ids must be prefixed.** Because asset ids are one global first-wins namespace, two mods
that both define `FuelTank` will not error, one will just vanish. Stock content prefixes everything
(`CoreFuelTankA_...`) and mods must too. Recommended form `<Id>_<Name>`, for example
`author.coolmod_FuelTank`. The registry should warn on unprefixed ids at upload and may harden this to a
rejection later.

---

## 4. Canonical layout

```
<Id>/
├── mod.toml                 [CONFIRMED] required
├── README.md                [CONVENTION] optional, rendered on the mod page
├── LICENSE.txt              [CONVENTION] optional, strongly recommended
├── CHANGELOG.md             [CONVENTION] optional
│
├── <EntryAssembly>.dll      [CONFIRMED] code mods: at the mod root
├── <dependency>.dll         [CONFIRMED] private dependencies, at the mod root
│
└── <any content layout>     [CONFIRMED] free-form, paths are declared in mod.toml
```

**DLLs go at the mod root.** Every published mod does this, and StarMap probing a subfolder is unverified. Do
not invent a `bin/` folder.

**Content layout is genuinely free.** Since `mod.toml` names every path, subfolders are organisational only.
This standard recommends but does not require:

```
Assets/        xml listed under `assets`
Systems/       xml listed under `systems`
Meshes/        .glb
Textures/      .ktx2, .png, .dds
Shaders/       .vert .frag .comp .glsl
Sounds/        .ogg .wav
Fonts/         .ttf listed under `fonts`
MeshCollections/     xml listed under `planetMeshes`
TextureCollections/  xml listed under `planetRandomHeightmapCollections`
```

Mirroring `Content/Core`'s own layout means anyone who has read stock content can navigate a mod.

---

## 5. `mod.toml`

**[CONFIRMED]** Required, at the mod folder root. Both loaders read this one file. Unknown keys are ignored by
both, which is why community keys can coexist with engine keys.

### 5.1 Keys KSA reads

These 17 are the complete set. Anything else is ignored by the game.

| Key | Type | Purpose |
|---|---|---|
| `name` | string | Display name |
| `assets` | string[] | XML files with an `<Assets>` root |
| `systems` | string[] | XML files with a `<System>` root |
| `fonts` | string[] | TTF files added to the font picker |
| `starBinaries` | string[] | Star field `.bin` files, additive across mods |
| `planetMeshes` | string[] | XML planet LOD mesh collections |
| `planetRandomHeightmapCollections` | string[] | XML random heightmap collections |
| `textureSizeCaps` | number[] | Texture cap options, `0` means uncapped |
| `supportedShadowMaps` | number[] | Shadow map resolution options |
| `supportedThumbnailSizes` | number[] | Part thumbnail resolution options |
| `simulationSpeeds` | number[] | Time warp multipliers |
| `supportedCloudQuality` | table[] | `{ name, value }` quality options |
| `supportedOceanSimQuality` | table[] | as above |
| `supportedExhaustQuality` | table[] | as above |
| `supportedKittenFurQuality` | table[] | as above |
| `supportedShadowSlots` | table[] | as above |
| `console` | table | `onBoot` and `onLoad` command arrays, see §9 |

All paths are relative to the mod folder. Order within `assets` matters: a file referencing an id defined in
another file must come after it.

There is no `parts`, `characters`, `heightmaps` or `sounds` key. Parts, characters and sounds are `<Part>`,
`<Character>` and `<Sound>` elements inside files listed under `assets`.

### 5.2 The StarMap block

**[CONFIRMED]** shape, from published mods:

```toml
name = "author.coolmod"

[StarMap]
EntryAssembly = "Author.CoolMod"   # DLL name without extension, at the mod root
```

**[CONFIRMED]** `[StarMap]` is optional even for code mods. `AircraftHUD` ships a working DLL with no block at
all, so the loader has some fallback. The registry must not require it.

**[@VERIFY]** `ExportedAssemblies`, and `[[StarMap.ModDependencies]]` with `ModId`, `Optional` and
`ImportedAssemblies`. No published mod uses either. Do not validate against these shapes until StarMap
confirms them.

### 5.3 Community keys

**[CONFIRMED]** in the wild, ignored by KSA:

```toml
version = "0.2.1"
author  = "MrJeranimo"
```

**[CONVENTION]** The registry record is authoritative for version, author, dependencies and compatibility.
These keys are advisory. If both are present and disagree, prefer the registry and warn the author.

---

## 6. Code facet

**Ship [CONFIRMED]:** the entry assembly, any assemblies it exports, and private dependency DLLs. Published
mods ship things like `Newtonsoft.Json.dll` and `DiscordRPC.dll` this way.

**Never ship [CONFIRMED]:** any game or engine assembly. `KSA.dll`, `Planet.*.dll`, `Brutal.*.dll`. These are
compile-time references the game already loads, and shipping them risks assembly conflicts.

**Never ship [CONVENTION]:** `StarMap.API.dll` and other loader assemblies, which the loader provides. Note
that `AircraftHUD` currently ships `StarMap.API.dll`, so enforcing this rejects at least one existing mod.
Warn before you reject.

**Build artefacts [CONVENTION]:** `.pdb` and `.deps.json` are tolerated and appear in published mods. Strip
them for release channel uploads to save size. Never ship `.csproj`, `.sln`, `obj/` or `bin/Debug/`.

**Target framework [CONFIRMED for current examples]:** .NET 10. Not enforced here, it is a loader concern.

---

## 7. Content facet

**[CONFIRMED] Every content file must be listed in `mod.toml` or it does nothing.** This is the single most
common way a content mod silently fails. The registry should surface it: parse `mod.toml`, resolve every
declared path, and warn about both missing targets and unreferenced content files.

**[CONFIRMED] Formats:**

| Kind | Format |
|---|---|
| Definitions | XML, roots `<Assets>` / `<System>` |
| Meshes | GLB (glTF 2.0 binary) |
| Textures | KTX2 in stock content, PNG and DDS also load |
| Sounds | OGG, WAV |
| Fonts | TTF |
| Star fields | `.bin`, custom binary |

**[CONFIRMED] Overriding stock assets** is done by declaring the same id from a mod that loads before `Core`,
because ids are first-wins. That requires the user's `manifest.toml` to list the mod above `Core`, which the
game rewrites freely. A registry should treat override mods as a distinct, fragile category and say so on the
mod page.

---

## 8. Reserved filenames

**[CONFIRMED]** Do not author these inside a mod folder except as specified:

| File | Meaning |
|---|---|
| `mod.toml` | The mod descriptor. Required. |
| `manifest.toml` | The game's enabled-mods list. Lives in the documents root, never inside a mod. Archives containing one are malformed. |
| `meta.toml` | Save and craft metadata. Only meaningful inside a save or craft folder. |
| `settings.toml` | Game settings. Documents root only. |

---

## 9. Security

**`[console]` is arbitrary command execution [CONFIRMED].**

```toml
[console]
onBoot = [ "..." ]
onLoad = [ "simspeed 1", "camera map 2.88 0.47 3635076" ]
```

These run developer console commands automatically at boot and after a system loads. Core uses it for the
opening camera angle. It is a legitimate feature and also the most direct scripted-behaviour vector in a
`mod.toml`.

**[CONVENTION] Registry policy:** parse `[console]`, display both arrays verbatim on the mod page, and flag
uploads that contain them for review. Do not silently strip.

**[CONVENTION] Code mods are unsandboxed.** A StarMap mod is a .NET assembly with full process privileges.
This is inherent to the loader, not something packaging can fix. The site should state it plainly and
consider signing or a trusted-author tier rather than pretending to sandbox.

**[CONVENTION] Archive extraction:**

- Reject absolute paths, `..` traversal, symlinks, hardlinks and device files.
- Reject entries whose normalised path escapes the single root directory.
- Enforce a compressed-to-uncompressed ratio ceiling and an uncompressed size ceiling.
- Reject nested archives.
- Strip or reject `__MACOSX/`, `.DS_Store`, `Thumbs.db`, `desktop.ini`.

---

## 10. Archive format

- **[CONVENTION]** Format `zip`, deflate or store. No zip64 unless the size genuinely needs it.
- **[CONVENTION]** UTF-8 filenames, forward slashes, relative paths only.
- **[CONVENTION]** Filename `<Id>-<version>.zip`.
- **[CONVENTION]** Exactly one top-level directory, named `<Id>` (§2). Multiple top-level entries, a
  wrong-named root, or files at the archive root are all rejections.
- **[CONVENTION]** Deterministic builds are encouraged: sort entries, zero the timestamps, so the same source
  produces the same hash.

---

## 11. Installer behaviour

This is where a naive implementation destroys user data.

**[CONFIRMED] Mods write into their own folder at runtime.** Published mods keep `config.json`, `hud.ini` and
`.log` files next to their DLL. A wipe-and-reextract update deletes user configuration.

**[CONVENTION] Install:**

1. Validate (§12).
2. Extract to a temporary directory, then move into place. Never extract directly into `mods/`.
3. Do not touch `manifest.toml`. The game adds new mods itself, disabled. If the client does write it, append
   with `enabled = false` and preserve existing order.
4. Record the archive hash and the file list for later diffing.

**[CONVENTION] Update:**

1. Compute the set of files the previous version installed, from the recorded list.
2. Delete only those, leaving anything the mod created at runtime.
3. Extract the new version.
4. Preserve `manifest.toml` position and enabled state.

**[CONVENTION] Uninstall:** remove only recorded files, then remove the folder if empty. If runtime files
remain, ask before deleting. Leave the `manifest.toml` entry alone, the game prunes it.

**[CONVENTION] Ordering:** if a mod must load before `Core`, for example an asset override, the client has to
edit `manifest.toml` order and re-check after every in-game mod toggle, because the game rewrites the file.
Present this to the user as a known-fragile state.

---

## 12. Registry validation checklist

| # | Check | Basis |
|---|---|---|
| 1 | Exactly one top-level directory, valid Id format | CONVENTION |
| 2 | `<root>/mod.toml` exists and parses as TOML | CONFIRMED |
| 3 | Root directory name equals the registry Id | CONVENTION |
| 4 | No path traversal, symlinks, junk, nested archives | CONVENTION |
| 5 | Size and compression-ratio ceilings respected | CONVENTION |
| 6 | No game or engine assemblies (`KSA.dll`, `Brutal.*`, `Planet.*`) | CONFIRMED |
| 7 | Loader assemblies (`StarMap.API.dll`) warn, do not reject yet | CONFIRMED |
| 8 | If `[StarMap].EntryAssembly` set, that DLL exists at the mod root | CONFIRMED |
| 9 | Every path in `assets`, `systems`, `fonts`, `starBinaries`, `planetMeshes`, `planetRandomHeightmapCollections` resolves to a file in the archive | CONFIRMED |
| 10 | Warn on content files present but not declared in `mod.toml` | CONFIRMED |
| 11 | Declared XML parses, and its root is `<Assets>` or `<System>` as appropriate | CONFIRMED |
| 12 | Warn on asset ids that are not prefixed with the mod Id | CONVENTION |
| 13 | `[console]` present, flag for review and display on the mod page | CONFIRMED |
| 14 | No `manifest.toml` or `settings.toml` inside the archive | CONFIRMED |
| 15 | `sha256` of the archive recorded as the integrity hash | CONVENTION |
| 16 | Declared facets match contents: code implies a DLL, content implies declared content | CONVENTION |

Checks 9 to 12 are what separate a registry from a file host. They catch the failure modes that otherwise
reach the user as "installed fine, does nothing".

---

## 13. Engine limitations the site must work around

**[CONFIRMED] Craft cannot ship as mods.** `DefaultVehicleSaves.SaveFolderPath` is hardcoded to
`Content/Core/defaultvehicles`, not per-mod. A vehicle folder inside a mod is never scanned. Hosting craft
therefore means either installing into the game's own `Content/Core`, which updates overwrite, or into
`Documents/.../vehicles/` as a player craft rather than a mod. Worth raising with the developers.

**[CONFIRMED] No dependency resolution in-game.** Load order is a flat user-editable list. Dependencies must be
resolved entirely by the client before writing `manifest.toml`.

**[CONFIRMED] No version negotiation.** Compatibility must be modelled in the registry. The game's own build
number is the useful key: `Content/Versions/*.json` ships one file per build with `build`, `date`,
`fromRevision` and `toRevision`. The revision integer (`5168`) is monotonic and is a better ordering key than
parsing the dotted build string.

**[CONFIRMED] Shared user directory.** All installs share one documents folder, one mods folder and one
`manifest.toml`. Instancing is a launcher-side concern. `KSA.Constants.DocumentsFolderPath` is the property a
launcher overrides, so any client that writes there must resolve it the same way rather than hardcoding.

---

## 14. Examples

**Code mod**

```
author.flightcomputer/
├── mod.toml
├── README.md
├── LICENSE.txt
├── Author.FlightComputer.dll
└── Newtonsoft.Json.dll
```

```toml
name = "author.flightcomputer"
version = "1.2.0"
author = "Author"

[StarMap]
EntryAssembly = "Author.FlightComputer"
```

**Content mod, a star system pack**

```
author.outerplanets/
├── mod.toml
├── README.md
├── Assets/
│   └── OuterPlanetsBodies.xml
├── Systems/
│   └── OuterPlanets.xml
├── Textures/
│   └── Persephone_Diffuse.ktx2
└── MeshCollections/
    └── OuterPlanetsMeshes.xml
```

```toml
name = "author.outerplanets"
version = "0.3.1"
author = "Author"

assets = [ "Assets/OuterPlanetsBodies.xml" ]
systems = [ "Systems/OuterPlanets.xml" ]
planetMeshes = [ "MeshCollections/OuterPlanetsMeshes.xml" ]
```

Note every content file appears in `mod.toml`. The textures do not, because they are referenced by path from
inside the XML rather than loaded directly.

**Hybrid**

```
author.livingsky/
├── mod.toml
├── Author.LivingSky.dll
├── Assets/
│   └── LivingSkyAssets.xml
└── Shaders/
    └── LivingSkyAtmosphere.frag
```

```toml
name = "author.livingsky"
version = "2.0.0"
author = "Author"
assets = [ "Assets/LivingSkyAssets.xml" ]

[StarMap]
EntryAssembly = "Author.LivingSky"
```

---

## 15. Open questions

1. **[@VERIFY]** Does StarMap probe subdirectories for `EntryAssembly`, or root only? Decides whether `bin/`
   is ever allowed.
2. **[@VERIFY]** What does StarMap do when `[StarMap]` is absent but a DLL is present, as in `AircraftHUD`?
3. **[@VERIFY]** Are `ExportedAssemblies` and `[[StarMap.ModDependencies]]` implemented, and in what shape?
4. **[@VERIFY]** Does StarMap enforce load order independently of `manifest.toml`?
5. **Open policy:** should the registry reject `StarMap.API.dll` in archives, given a published mod ships it?
6. **Open policy:** manual review threshold for `[console]`.
7. **Engine ask:** per-mod `defaultvehicles`, so craft can ship as mods (§13).

Questions 1 to 4 are one conversation with the StarMap authors. Everything marked `[CONFIRMED]` can be frozen
and enforced today.
