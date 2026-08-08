# KSA Mod Archive (.zip) Structure Standard

**Status:** Draft v0.4
**Companion to:** KSA Mod Registry Specification
**Verified against:** KSA build `2026.8.5.5168`, StarMap `0.4.6`
**Aligned with:** KSAModding [RFC 0017](../content-manager-design/rfcs/0017-game-version-ordering-and-compatibility.md),
[RFC 0025](../content-manager-design/rfcs/0025-scope.md),
[RFC 0031](../content-manager-design/rfcs/0031-content-metadata-format.md), all Accepted
**Scope:** the internal layout of a distributable KSA mod archive, what a registry must validate on upload,
and how a client must install, update and remove one.

---

## 0. Grounding

Every rule is tagged:

- **[CONFIRMED]** read from decompiled `KSA.dll` for build 2026.8.5.5168, or observed in published mods.
- **[CONVENTION]** a choice this standard makes. The loaders neither require nor forbid it.
- **[RFC]** settled by an Accepted KSAModding RFC. This standard follows rather than re-decides.
- **[@VERIFY]** depends on internals not readable from the sources above.

Nothing below is inferred from folder names or naming similarity. Where a claim could not be checked it is
marked `[@VERIFY]` rather than guessed.

**On the two source sets.** The KSAModding `content-manager-design` repository carries both an RFC process and
a research corpus verified against build `2026.8.3.5117` and StarMap `0.4.6`. This standard was written
independently against `2026.8.5.5168`; where the two agree, the claim is corroborated by two separate
decompilation efforts, which is worth more than either alone. Where the RFCs have *decided* something — id
format, version ordering, where metadata lives — this standard now follows the RFC and says so, because a
second incompatible answer helps nobody. Section 15 records what the research resolved that this document
previously had open, and it is most of it.

---

## 1. How KSA actually loads a mod

A packaging standard that does not match this will produce archives that install and then do nothing. Read
this section before the rules.

**[CONFIRMED] Mod identity is the folder name, and the game overwrites anything else.** `Mod.MakeUsing`
deserializes `mod.toml` and *then* assigns the id from the folder name, discarding whatever the file declared.
This is stronger than "there is no id field": a `mod.toml` may carry one and it will be silently ignored. The
folder name is the key in `manifest.toml`, the key StarMap matches `ModId` against, and the namespace for
everything the mod registers. Renaming the folder produces a different mod.

The consequence for packaging is decisive: **identity declared inside an archive can lie**, so it cannot be
trusted as the source of truth. This is the load-bearing reason RFC 0031 puts registry metadata in the index
rather than in the archive (§5.3).

**[CONFIRMED] Two locations, `Content` wins.** For each enabled entry the game looks for
`<GameDir>/Content/<Id>/mod.toml` first, then `<Documents>/My Games/Kitten Space Agency/mods/<Id>/mod.toml`.
Third-party mods belong in the second.

The `Content` path is a bare **relative** path in `ModLibrary.PrepareManifest`, `ModLibrary.PrepareAll` and
`ModEntry.Exists`. `Program.Main` sets the working directory to the executable's own folder as its first
statement, so it resolves next to the game binary however the game was launched.

The user path comes from `ModLibrary.LocalModsFolderPath` on top of `Constants.DocumentsFolderPath`, which
resolves `Environment.SpecialFolder.Personal` and appends `My Games/Kitten Space Agency`. On Linux .NET
resolves that special folder to `$HOME`, so the path is `~/My Games/Kitten Space Agency` — **not** anything
XDG-shaped. A client that hardcodes an XDG path is wrong on every Linux install.

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

**[CONFIRMED] The game rewrites `manifest.toml`, and reconciles it every launch.** `ModManifest.Save`
regenerates the whole file from the in-memory list and emits only `id` and `enabled` per entry, so comments,
foreign keys and formatting are destroyed. A manager cannot store its own state there and must keep a sidecar.

`ModLibrary.PrepareManifest` runs once per session and: appends entries present in `Content/manifest.toml` but
missing from the user's; marks entries appearing in both as **core**, exempting them from removal; scans both
locations via `ModLibrary.AddMods`; then **removes every non-core entry whose folder no longer exists**,
saving after each removal.

**[CONFIRMED] Ids are written unescaped.** `ModManifest.Save` writes the id straight into a quoted TOML string
with no escaping, so a folder name containing a quote corrupts the file. Windows forbids such names; Linux and
macOS do not. This is why the id charset in §3 is stricter than any single filesystem requires.

**[CONFIRMED] Two game installs actively interfere, with no manager involved.** The mods folder and the
manifest are per *user*, not per install. Because `PrepareManifest` prunes non-core entries whose folder is
missing, and `ModEntry.Exists` looks in the launched install's `Content/` plus the shared user folder, a mod
living in install A's `Content/` loses its manifest entry the next time install B launches.

**[CONFIRMED] The game offers no isolation hatch of its own.** It accepts exactly two command line arguments,
`-build-info` and `-fixed-viewport`, neither affecting any path, and it reads **no environment variables at
all** — there is not one lookup in the shipped assemblies. Instancing is possible only through StarMap (§13).

**[CONFIRMED] KSA has no concept of a mod version.** No version key, no compatibility field, no dependency
mechanism, and nothing on disk records which version of a mod is installed. A manager cannot recover that by
inspecting an installation it did not perform. All of it lives in the registry.

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

**[RFC] RFC 0031 arrives at the same layout independently and makes it checkable.** Its generated release file
carries `install.root` with a `derived` flag, set `true` when the watcher finds exactly this shape: one
top-level directory containing `mod.toml`, its name matching the id. **A name mismatch is a validation
error**, because the folder name is the identity the game will see. An optional authored `[install] root` key
covers archives with an unusual layout, so the strict default costs nothing in flexibility — but a conforming
archive never needs it.

---

## 3. Id rules

**[CONFIRMED] The Id is the folder name, and it is the global namespace.** Registration hashes the string, so
`MyMod` and `mymod` are different mods to the asset lookup, while the filesystem probe that finds the folder
is case-insensitive on Windows and case-sensitive on Linux.

**[RFC] Id format — RFC 0031.** This standard adopts RFC 0031's rules verbatim rather than defining its own.
An earlier draft of this document mandated a lowercase `<author>.<mod>` form; that is withdrawn. It would have
excluded the entire existing corpus, which uses names like `AdvancedFlightComputer` and `AircraftHUD`, and
those mods cannot rename because the folder name *is* the identity.

- 1 to 64 characters, ASCII letters, digits, `-`, `_` and `.` only.
- First and last character must be a letter or a digit.
- Ids compare **case-insensitively**; the authored casing is preserved for display and on disk.
- The namespace is global across content types: a mod, a pack and a loader can never share an id, so every
  reference to an id stays type-free.
- Reserved, compared case-insensitively **against the id up to its first `.`**: `Core`, and the Windows device
  names `CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`.

```text
^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$
```

Each boundary earns its place, and the reasoning is worth keeping because it is not guessable:

- **ASCII only**, because macOS normalises non-ASCII folder names differently from other platforms, so one id
  could be two distinct byte sequences on two machines.
- **No leading dot**, because that hides the folder on Unix.
- **No trailing dot and no space**, because Windows silently strips or rejects them.
- **Device names checked up to the first dot**, because Windows treats dotted forms such as `CON.mod` as
  devices too. Checking the whole string misses this.
- **Case-insensitive uniqueness**, because folder names are case-insensitive on Windows and case-sensitive on
  Linux, so `MyMod` and `mymod` would be one mod on one machine and two on another. Note this makes the
  registry namespace deliberately *narrower* than the game's asset table, which does distinguish them. The
  registry models what can be installed side by side, not what a hash can tell apart.
- **64 characters**, because the id lands inside real paths under the user's Documents folder and Windows caps
  a path at 260 characters unless long paths are enabled.

Everything expressive the id forbids belongs in the display name, which has no character restrictions.

**[CONFIRMED] `Core` is reserved.** It is the game's own mod: the game ships `Content/Core` and
`ModLibrary.PrepareManifest` exempts its manifest entry from cleanup.

**[CONVENTION] Asset ids must be prefixed.** Because asset ids are one global first-wins namespace, two mods
that both define `FuelTank` will not error, one will just vanish. Stock content prefixes everything
(`CoreFuelTankA_...`) and mods must too. Recommended form `<Id>_<Name>`, for example
`author.coolmod_FuelTank`. The registry should warn on unprefixed ids at upload and may harden this to a
rejection later. Note that already-published asset ids cannot be changed without breaking the saves and craft
that reference them, so hardening to a rejection can only ever bind new ids. The cross-mod collision index
(§12 check 12, plan §7) is the durable mitigation; the naming convention only helps mods written after it.

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

**[CONFIRMED] `[StarMap]` is optional even for code mods, and the fallback is now known.** `AircraftHUD` ships
a working DLL with no block at all. StarMap's own source resolves this: **when the section is absent,
`EntryAssembly` defaults to the mod id.** `AircraftHUD` works because its DLL is named after its folder. The
registry must not require the block, and it can now tell an author exactly why their mod loads without one.

**[CONFIRMED] The full section shape**, from StarMap `0.4.6`, parsed with Tomlet into `StarMapConfig`:

| Field | Meaning |
|---|---|
| `EntryAssembly` | Assembly to load, without `.dll`. Defaults to the mod id when the section is absent. |
| `ExportedAssemblies` | Which of the mod's assemblies dependents may use. |
| `[[StarMap.ModDependencies]]` | One block per dependency: `ModId`, `Optional` (default `false`), `ImportedAssemblies`. |

Both were previously marked `[@VERIFY]` here. They are real and implemented. `ModId` is matched against the
mod id, which is the folder name.

**This section is the only place in the entire KSA ecosystem where one mod declares a dependency on another**,
which makes it the only existing source of dependency data. A registry should read it as ground truth rather
than duplicate it, because duplicated data can disagree with what the loader actually does at runtime.

**[CONFIRMED] What the loader's dependency handling does not have:** no versions anywhere (dependencies are
name-only, so "requires X 1.2 or newer" is inexpressible), no conflict detection, and no failure surface — a
missing required dependency, a missing entry assembly, or a mod class without `[StarMapMod]` all end as a
`Console.WriteLine` in a window most users never see. Every one of those is a registry opportunity.

### 5.3 Community keys, and why registry metadata does not go here

**[CONFIRMED]** in the wild, ignored by KSA:

```toml
version = "0.2.1"
author  = "MrJeranimo"
patches = [ ... ]     # a convention of the community KittenExtensions mod
```

**[RFC] Registry metadata does not live in the archive at all — RFC 0031.** Nothing is added to `mod.toml`,
and the loader's section stays loader configuration rather than manager metadata. Three reasons, and they are
good ones:

1. **In-archive identity can lie.** `Mod.MakeUsing` overwrites any declared id with the folder name (§1), so a
   self-describing archive can claim an identity the game will never honour.
2. **Every metadata fix would force a re-release.** A corrected link or a tightened compatibility bound should
   be an index edit, not a new archive that every user has to re-download.
3. **The loader author's position**, recorded in RFC 0025: `mod.toml` should not carry manager metadata.

So the `version` and `author` keys above are advisory only, and a registry reads them as a hint at most. The
index record is authoritative for version, author, links, license, dependencies and compatibility. Where the
two disagree, the index wins and the author gets a warning — but the durable answer is that the index is the
only place those facts are written.

A validator reads `mod.toml` as a **data source and never writes to it**, which is the same contract RFC 0031
gives its watcher.

---

## 6. Code facet

**Ship [CONFIRMED]:** the entry assembly, any assemblies it exports, and private dependency DLLs. Published
mods ship things like `Newtonsoft.Json.dll` and `DiscordRPC.dll` this way.

**[CONFIRMED] Shipping private dependencies is safe, by design.** Each mod gets its own
`ModAssemblyLoadContext`. Resolution order is: assemblies already in the default context, then the shared core
context, then a load attempt against the core context, then assemblies its declared dependencies exported to
it, then the mod's own dependency graph. **Two mods can therefore ship different versions of the same library
without colliding** — this is the strongest technical argument for StarMap over a naive loader, and it means a
registry should not warn about duplicate third-party DLLs across mods.

**Never ship [CONFIRMED]:** any game or engine assembly. `KSA.dll`, `Planet.*.dll`, `Brutal.*.dll`. These are
compile-time references the game already loads, and the isolation above explicitly resolves the default and
core contexts *first*, so a shipped copy is dead weight at best and a conflict at worst.

**Never ship [CONVENTION]:** `StarMap.API.dll` and other loader assemblies. Mods reference the `StarMap.API`
**NuGet package** at compile time and the loader provides it at runtime through the core context, so the
correct project setup never copies it to output. `AircraftHUD` currently ships it, so enforcing this rejects
at least one existing mod. Warn before you reject, and point at the NuGet reference as the fix.

**Build artefacts [CONVENTION]:** `.pdb` and `.deps.json` are tolerated and appear in published mods. Strip
them for release channel uploads to save size. Never ship `.csproj`, `.sln`, `obj/` or `bin/Debug/`.

**Target framework [CONFIRMED for current examples]:** .NET 10. Not enforced here, it is a loader concern.

---

## 7. Content facet

**[CONFIRMED] Every content file must be listed in `mod.toml` or it does nothing.** This is the single most
common way a content mod silently fails. The registry should surface it: parse `mod.toml`, resolve every
declared path, and warn about missing targets and genuinely unreachable files.

**Reachability, not declaration, is the test.** Only the six list keys are loaded directly from `mod.toml`.
Meshes, textures, shaders and sounds are correctly absent from it — they are referenced by path from inside
the XML that *is* declared, as in §14's star system pack. A validator that warns on every file not named in
`mod.toml` fires on the normal, correct shape of every content mod, and authors learn to ignore it.

Compute reachability instead, in two hops:

1. Files named in `assets`, `systems`, `fonts`, `starBinaries`, `planetMeshes` and
   `planetRandomHeightmapCollections` are reachable.
2. Paths referenced from inside those files' XML are reachable, transitively.

Warn only on content files reachable from neither. That set is small, and every entry in it is a real
mistake — a texture the author forgot to wire up, or a leftover from a renamed asset.

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
3. Write `manifest.toml` deliberately, per the rules below.
4. Record the archive hash and the file list for later diffing.

**Writing `manifest.toml`: what a decade of CKAN plus a KSA fork actually converged on.** An earlier draft
here said "do not touch it, the game adds new mods itself, disabled." That is defensible but it ships a bad
experience: the user installs a mod, launches, and nothing happens, because a new entry is created *disabled*
and only takes effect after a further relaunch. The CKAN-KSA fork landed on a more useful contract after a
dedicated hardening round, and it is the one to copy:

- **Write `enabled = true` for mods the client installs.** This is the whole point of installing one.
- **Never flip the enabled state of an entry the client did not create.** An in-game disable must survive any
  client operation. The user's choice outranks the client's.
- **Remove entries the client manages on uninstall**, and only those.
- **Re-derive ids from on-disk folder names**, because that is what the game keys on.
- **Track which entries the client manages in a sidecar file.** The manifest cannot carry foreign keys —
  `ModManifest.Save` regenerates it from scratch and emits only `id` and `enabled` (§1).
- **Parse tolerantly:** the file may carry quoting and comments the client did not write, and matching must be
  case-insensitive on Windows.

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

**[CONFIRMED] A relaunch is required, and StarMap can perform it.** A freshly discovered folder is enabled
only after the game has been restarted. StarMap patches `ModLibrary.PrepareAll`: when it detects mods that
were newly discovered and enabled this session, it shows a confirm dialog and, on confirm, spawns a fresh
`StarMap.exe --restarted` and exits. File operations for the mod change happen in the new process before mods
load, which is why no supervising process exists to plug into. A client's contract is therefore **write files,
then launch** — anything deeper needs a conversation with the loader author.

**[CONFIRMED] The running process is `StarMap.exe`, not the game.** StarMap hosts the game in-process rather
than launching it: it loads `KSA.dll` into a load context it controls, sets `APP_CONTEXT_BASE_DIRECTORY` to
the game folder, applies its patches, and only then invokes the game's entry point. Any client doing
process-name detection of "is the game running" must know this, and mods are already loaded and Harmony
patches already applied before the game's `Main` gets control.

---

## 12. Registry validation checklist

| # | Check | Basis |
|---|---|---|
| 1 | Exactly one top-level directory, Id valid under §3 | RFC |
| 2 | `<root>/mod.toml` exists and parses as TOML | CONFIRMED |
| 3 | Root directory name equals the registry Id, compared case-insensitively | RFC |
| 4 | No path traversal, symlinks, junk, nested archives | CONVENTION |
| 5 | Size and compression-ratio ceilings respected | CONVENTION |
| 6 | No game or engine assemblies (`KSA.dll`, `Brutal.*`, `Planet.*`) | CONFIRMED |
| 7 | Loader assemblies (`StarMap.API.dll`) warn, do not reject yet | CONFIRMED |
| 8 | Entry assembly exists at the mod root: `[StarMap].EntryAssembly` if set, otherwise `<Id>.dll` | CONFIRMED |
| 9 | Every path in `assets`, `systems`, `fonts`, `starBinaries`, `planetMeshes`, `planetRandomHeightmapCollections` resolves to a file in the archive | CONFIRMED |
| 10 | Warn on content files unreachable from both `mod.toml` and the declared XML (§7) | CONFIRMED |
| 11 | Declared XML parses, and its root is `<Assets>` or `<System>` as appropriate | CONFIRMED |
| 12 | Warn on asset ids that are not prefixed with the mod Id | CONVENTION |
| 13 | `[console]` present, flag for review and display on the mod page | CONFIRMED |
| 14 | No `manifest.toml` or `settings.toml` inside the archive | CONFIRMED |
| 15 | `sha256` of the archive recorded as the integrity hash | CONVENTION |
| 16 | Declared facets match contents: code implies a DLL, content implies declared content | CONVENTION |
| 17 | `[[StarMap.ModDependencies]]` extracted and recorded, each with its `Optional` flag | CONFIRMED |
| 18 | Archive re-stamp refused: a version already published never gets new bytes (§5.3, plan §5) | RFC |

Checks 9 to 12 are what separate a registry from a file host. They catch the failure modes that otherwise
reach the user as "installed fine, does nothing".

Check 17 is cheap and load-bearing. `[[StarMap.ModDependencies]]` is the only machine-readable dependency data
that exists anywhere in the ecosystem (§5.2), and the loader acts on it at runtime, so it is ground truth for
code dependencies rather than a hint. Read it; do not ask authors to retype it. RFC 0031's merge rule is the
one to follow: derived entries come from the archive per release, authored entries may add version bounds the
loader cannot express, an authored entry replaces the derived entry with the same id, and **there is no way to
suppress a derived entry**, because the loader will act on it whatever the index says.

---

## 13. Engine limitations the site must work around

**[CONFIRMED] Craft cannot ship as mods, and the fix is a separate content type.**
`DefaultVehicleSaves.SaveFolderPath` is hardcoded to `Content/Core/defaultvehicles`, not per-mod, so a vehicle
folder inside a mod is never scanned. This finding stands. RFC 0025 resolves it at the product level rather
than the engine level: **vehicles and saves are first-class content types of their own**, with their own
install target under the user-global root, not mods that happen to contain craft. That is the right shape,
because it needs no engine change to ship. The per-mod `defaultvehicles` engine ask remains worth raising, but
nothing is blocked on it.

**[CONFIRMED] The game has no dependency resolution; StarMap has a partial one.** An earlier draft here said
load order is a flat list and dependencies must be resolved entirely by the client. The first half is right
about the *game*. StarMap does more than nothing: `ModLoader` walks the manifest once, initialises mods whose
dependencies are all present, and parks the rest in a `WaitingModsDependencyGraph` keyed by what they wait on.
`CheckForDependentMods` releases waiters as each mod finishes, then `TryLoadWaitingMods` loops — loading any
mod whose remaining unmet dependencies are all `Optional` — until a pass loads nothing new. Whatever is left
never loads, and the only signal is a console line.

So the loader **does** reorder initialisation within the manifest walk. What it cannot do is versions,
conflicts, or telling the user anything. A client still has to resolve the set and the ordering, but it should
not assume manifest order alone determines initialisation order.

**[CONFIRMED] No version negotiation, and ordering is subtler than it looks.** See §16 — this turned out to be
the single most counter-intuitive area of the game and it now has its own section.

**[CONFIRMED] Shared user directory, and StarMap is the only way out.** All installs share one documents
folder, one mods folder and one `manifest.toml`, and §1 records that two installs actively prune each other's
manifest entries. The game itself offers no override: no useful CLI argument, no environment variable.

The one mechanism that exists is StarMap's. `DocumentsPathPatches` reads an override from, in order, the CLI
flag `-InstancePath <path>` (matched case-insensitively against raw process arguments) and then the
environment variable `STARMAP_INSTANCE_PATH`. If either is set, a Harmony prefix is applied to the getter of
`KSA.Constants.DocumentsFolderPath` from `ModLoader.Init`, before mod discovery and before the game's entry
point runs.

Because that property is the root of every user-writable path — mods, manifest, saves, settings, vehicles,
layouts, languages, screenshots, logs, crash dumps — overriding it **moves an entire profile, not just the
mods**. Two consequences for a client:

- It can drive instancing without touching StarMap's config: set `STARMAP_INSTANCE_PATH` on the spawned
  process, or pass `-InstancePath`, per launch.
- A mod that writes somewhere without going through `Constants.DocumentsFolderPath` escapes the override and
  keeps writing to the shared location. Instancing is therefore best-effort, not a sandbox.

Note also that `RepositoryLocation` in `StarMapConfig.json` sounds like it relocates mods and does not; it is
referenced nowhere and is dead configuration. StarMap adds no mod location of its own — it looks exactly where
the game looks.

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

Note every directly-loaded content file appears in `mod.toml`. The textures do not, and correctly so: they are
referenced by path from inside `OuterPlanetsBodies.xml` rather than loaded directly. This is the normal shape
of a content mod, and it is why validation tests reachability rather than declaration (§7).

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

### Resolved since v0.3

The `content-manager-design` research answered four of the five `[@VERIFY]` items outright. They are recorded
here rather than deleted, because knowing a question was asked and settled is worth more than a silent edit.

| Was | Now |
|---|---|
| Does StarMap probe subdirectories for `EntryAssembly`? | **Partly answered.** `EntryAssembly` is a bare assembly name resolved through the mod's own `ModAssemblyLoadContext` dependency graph. Root-level is what every published mod does and what is confirmed working. Still no evidence a `bin/` layout resolves, so §4's "DLLs at the mod root" stands. |
| What happens when `[StarMap]` is absent but a DLL is present? | **Answered.** `EntryAssembly` defaults to the mod id, which is why `AircraftHUD` loads (§5.2). |
| Are `ExportedAssemblies` and `[[StarMap.ModDependencies]]` implemented? | **Answered, both real.** Full shapes in §5.2. `ModDependencies` carries `ModId`, `Optional` and `ImportedAssemblies`. |
| Does StarMap enforce load order independently of `manifest.toml`? | **Answered, and the earlier assumption was wrong.** It walks the manifest in order but defers mods with unmet dependencies via a waiting graph, so initialisation order is not manifest order (§13). |

### Still open

1. **Open policy:** should the registry reject `StarMap.API.dll` in archives, given a published mod ships it?
   Now easier to answer well: mods reference it as a NuGet package and the loader supplies it at runtime, so
   shipping it is a project-configuration mistake with a one-line fix. Warn, link the fix, do not reject.
2. **Open policy:** manual review threshold for `[console]`.
3. **Engine ask:** per-mod `defaultvehicles` (§13). No longer blocking — RFC 0025 routes craft through a
   separate content type — but still the cleaner fix.
4. **[@VERIFY]** Does anything other than `Constants.DocumentsFolderPath` need patching for a complete
   instance? StarMap's research notes that `ModLibrary.CheckDirectories` creates the root and mods folders when
   missing, but the other consumers of that property were not checked. A partially-created instance path is a
   plausible source of confusing client bugs.
5. **[@VERIFY]** Whether a vehicle or save file records which mods it needs, or whether the publisher has to
   declare that by hand. Open in RFC 0025 too, and it decides how much of §7's dependency story extends to the
   new content types.

Everything marked `[CONFIRMED]` or `[RFC]` can be frozen and enforced today.

---

## 16. Game versions and compatibility

**[RFC] This section follows RFC 0017, which is Accepted.** It is summarised rather than re-argued, because
the underlying research is quantified against all 155 shipped releases and this standard has nothing to add to
it. It sits here because archive validation and installer behaviour both depend on it.

### The version does not sort the way it looks

A KSA version is `Year.Month.Build.Revision`, optionally `-Suffix` and `+hash`:

```text
^v?(?<Year>\d+)\.(?<Month>\d+)\.(?<Build>\d+)\.(?<Revision>\d+)(?:-(?<Suffix>[^+]+))?(?:\+.*)?$
```

**Order by the revision alone.** Not by the full string, not by the first three components.

- The **build counter is machine-local**, not part of release identity. The game's own build tooling replaces
  it with a literal `X` when naming the changelog file, so `2026.8.3.5117` ships as
  `Content/Versions/v2026.8.X.5117.json`.
- Across the shipped history the build counter **decreases 32 times** while the revision rises, and only 46
  distinct values appear across 155 releases, one of them reused 16 times.
- Sorting the full four-part string puts **21 adjacent pairs in the wrong order**. Sorting by revision alone
  reproduces the true order for all 155.
- The game agrees: `VersionInfo.CompareTo` compares revision first, the build counter only as a tiebreak, then
  the suffix. **Year and month are display only.**

Two limits worth stating: a version is not a totally ordered value, because specialist builds exist for
hardware vendors, agencies and universities, and the same revision compiled development, release or production
behaves differently. Compatibility here is scoped to the public production stream, where the revision is
unique and total.

`Year.Month.Build` as a range prefix is meaningless: 20 of the 133 distinct combinations match more than one
release, and 19 of those select a set that is **not contiguous in time**. Only two granularities are worth
offering — a month, or an explicit revision.

### Detecting what is installed

In order of preference:

1. **`KSA.dll` PE FileVersion.** Exact four-part string, readable without loading the assembly, no suffix to
   strip. Use this.
2. `KSA.dll` PE ProductVersion — same value with `+hash` appended.
3. The `build` field of the newest `Content/Versions/*.json`. Note the file *name* carries `X` instead of the
   build counter, so the name is not a substitute for the field.

### Expressing compatibility

A lower bound, required. An upper bound, optional, absent meaning open — and open is the recommended default.

Authors write a version the way the game displays it (`2026.8.3.5117`), or a month (`2026.7`) meaning the
whole of that calendar month. **Tooling resolves both to a revision at publish time** and stores the resolved
integer alongside the display string. This is what lets a client evaluate compatibility offline and for a
release the index has not caught up with; if published metadata carried month strings, every check would need
an index lookup.

| Condition | State | Behaviour |
|---|---|---|
| no usable minimum | Unknown | Listed, installable after confirmation |
| `r < min` | Incompatible | **Blocked**, with the version it needs |
| `max` absent, or `r <= max` | Compatible | Installs normally |
| `r > max` | Untested | Installs after confirmation |

**Only Incompatible blocks.** The rationale is worth internalising because it governs the whole design: the
game validates nothing, so a false "incompatible" takes a working mod away from a user for no reason, while a
false "compatible" is a mod that does not load and can be removed again. Warn, do not block.

**The cadence is why ranges are mandatory.** 155 releases in under twelve months is roughly thirteen a month.
Compatibility-as-equality — declaring the one build a mod was compiled against — marks the entire catalogue
broken within days of any update, almost always wrongly.

### The release index is already on disk

`Content/Versions/` is not just changelog text. Each file carries `build`, `date`, `fromRevision` and
`toRevision`, and `VersionHistory.Initialize` reads them recursively and sorts by `toRevision` descending. The
pairs chain from one release to the next, with 5 breaks across the 155 shipped files.

**Every installed copy of the game therefore contains a dated, ordered index of every release up to its own**,
keyed by revision, needing no network and no service. Two things it cannot do:

- It knows nothing newer than the installed game. For "is there an update", the master server at
  `http://ksa-master1.rocketwerkz.com:8082/version` is the authority, and it reports exactly one version: the
  current public production build.
- A user who never installed a given release has no record of it, so resolving a month to a revision for an
  uncovered period needs an external list.

That external list already exists and costs nothing to consume:
[`builds.json`](https://raw.githubusercontent.com/KSAModding/KSA-CKAN-meta/main/builds.json) in
KSAModding/KSA-CKAN-meta, updated hourly by a GitHub Action polling the same master server endpoint, current
since 2026-07-02 without human attention. Use it rather than rebuilding one.

### Display

Show the version as the game shows it, build counter included. It is meaningless for ordering but it is what
the user sees in-game, in the launcher and in a bug report. Render ranges in full version strings, never raw
revisions.
