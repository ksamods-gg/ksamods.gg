# ksamods.gg Feature Plan

**Status:** Draft v0.1
**Companion to:** KSA Mod Archive Structure Standard v0.2, KSA Mod Registry Specification
**Grounded on:** KSA build `2026.8.5.5168`, StarMap `0.4.6`

---

## 1. What this site is

**ksamods.gg is an index, not a file host.** Archives live on GitHub releases or on the author's own server.
The site stores metadata, a pinned hash, and a link.

That one decision drives most of this document, so it is worth being blunt about what it buys and what it
costs.

**What it buys:** no storage bill, no bandwidth bill, no DMCA safe-harbour exposure for the binaries
themselves, and authors keep control of their own distribution.

**What it costs:** the site cannot guarantee a download exists, cannot guarantee the bytes at a URL today are
the bytes that were reviewed, and cannot fix a dead link. Every feature below that mentions **integrity**,
**availability** or **tamper detection** exists to buy back some of what hosting would have given for free.

**The site's actual product is trust and resolution**, not files. It answers: does this mod exist, what
version, does it work with my game build, what does it depend on, is the file I just downloaded the file that
was reviewed, and will it conflict with the other mods I have.

---

## 2. Domain model

```
Author        a person or org, owns Mods
  └── Mod     stable identity, == the KSA folder Id, immutable once claimed
        └── Version   semver-ish, immutable once published
              └── Artifact   one downloadable archive: url + sha256 + size
```

**Mod.Id is the KSA folder name.** Not a slug, not a display name. It is the global namespace inside the game,
so the registry namespace and the game namespace must be the same thing or dependency resolution is
meaningless. First claim wins, and the id is never transferable to a different mod, only to a different owner.

**Versions are immutable.** Once published, a version's artifact URL and hash are frozen. A changed file means
a new version. This is the only way an index without hosting can offer any integrity guarantee at all.

**Artifacts may be multiple per version**, for example a GitHub release asset plus an author mirror. Same hash
required across all of them. Different hash means different artifact, which means a different version.

---

## 3. Publishing

### 3.1 Claiming an id

1. Sign in with GitHub or Discord.
2. Request an id. Validated against the Standard §3 format rules.
3. Reserved: `Core`, anything colliding case-insensitively with an existing id, Windows device names.
4. Claim is immediate for unused ids. Disputes go to moderation.

Id squatting policy: an id with no published version after 90 days can be reclaimed on request.

### 3.2 Submitting a version

Two paths, and the GitHub one should be the default because it is verifiable.

**Path A, GitHub App (recommended).** Author installs the ksamods GitHub App on the mod repo. On a published
release, the site fetches the release assets, runs the verification pipeline, and creates the version
automatically. The site knows the repo, the tag, the commit and the asset id, so provenance is real.

**Path B, manual URL.** Author pastes a direct download URL. The site fetches, verifies, records the hash.
Provenance is weaker and the mod page should say so. Ownership of the domain is not proven.

Both paths run the same pipeline (§4). Neither path stores the archive permanently.

### 3.3 Version metadata

Author supplies, or the pipeline infers where possible:

| Field | Source |
|---|---|
| Version string | author, or the release tag |
| Changelog | author, or the GitHub release body |
| Compatible game builds | author, expressed as a revision range (§6) |
| Dependencies | author, as `id + version range` |
| Conflicts | author, plus auto-detected id collisions (§7) |
| Load order hints | author: before, after, or requires-before-Core |
| Channel | stable, beta, dev |
| License | SPDX identifier, or the detected `LICENSE` file |

---

## 4. Verification pipeline

Runs on every submission, in a sandbox, with no code execution. This is where the Archive Standard becomes
software.

**Stage 1, fetch.** Download from the artifact URL with a size ceiling and a timeout. Record final URL after
redirects, HTTP status, `Content-Length`, `ETag`, `Last-Modified`.

**Stage 2, integrity.** Compute `sha256`. If this is a re-check of an existing version and the hash has
changed, this is a **tamper event** (§5).

**Stage 3, archive safety.** Reject path traversal, absolute paths, symlinks, hardlinks, device entries,
nested archives, and anything whose normalised path escapes the root. Enforce compression ratio and
uncompressed size ceilings.

**Stage 4, structure.** Standard §2 and §12: exactly one top-level directory, name equals the claimed Id,
`mod.toml` present and parses.

**Stage 5, declaration integrity.** This is the check nobody else can do for KSA, and it is the most valuable
thing in the pipeline.

- Resolve every path declared under `assets`, `systems`, `fonts`, `starBinaries`, `planetMeshes`,
  `planetRandomHeightmapCollections`. A declared path that does not exist is an **error**.
- Parse every declared XML. Malformed XML is an **error**. Wrong root element is an **error**.
- List content files present in the archive but declared nowhere. This is a **warning**, and it is the single
  most common reason a content mod installs cleanly and does nothing.

**Stage 6, asset id extraction.** Walk the parsed XML, collect every `Id` attribute, and store it against the
version. Feeds conflict detection (§7).

**Stage 7, code facet.** If a DLL is present: confirm `[StarMap].EntryAssembly` resolves if set, reject game
and engine assemblies (`KSA.dll`, `Brutal.*`, `Planet.*`), warn on loader assemblies, list all shipped
assemblies on the mod page.

**Stage 8, risk surface.** Extract and record for display: `[console]` `onBoot` and `onLoad` arrays, the full
list of shipped DLLs, whether the mod declares ids that collide with `Core`.

Output: pass, pass-with-warnings, or fail, with a per-check report the author can act on. Warnings do not
block publication but are shown on the mod page.

---

## 5. Integrity and tamper detection

The hard problem of not hosting: the bytes can change after review.

**Hash pinning.** Every artifact is pinned to a `sha256`. The client verifies after download and refuses to
install on mismatch.

**Periodic re-verification.** Re-fetch published artifacts on a schedule, weekly for stable, and compare
hashes. Three outcomes:

- **Match:** update a `last_verified` timestamp, shown on the mod page.
- **Gone (404, 410, dead host):** mark the artifact `unavailable`, notify the author, show a warning on the
  page. The version stays in the index because clients may have it cached and dependency graphs still need to
  resolve it.
- **Changed hash:** **tamper event.** Immediately mark the version `quarantined`, hide the download, notify
  the author and moderators, and require a human to either accept it as a new version or confirm a compromise.
  This is the single most important background job on the site.

**Client-side enforcement.** The API always returns the pinned hash. A client that does not verify is
non-conforming. Document this as a hard requirement, not a suggestion.

**Optional archival mirror.** Even without being a general host, consider storing a copy of verified artifacts
under a permissive-license opt-in, purely as a fallback for dead links. This is the difference between a dead
mod and a lost mod. Make it author-opt-in at upload so the licensing question is answered up front.

---

## 6. Compatibility model

**KSA has no version negotiation at all.** No compatibility field, no minimum-version check, nothing. The
registry is the only place this can live.

**Use the build revision integer, not the dotted string.** The game ships `Content/Versions/*.json`, one file
per build, each carrying `build`, `date`, `fromRevision` and `toRevision`. The revision (`5168`) is monotonic
and sorts correctly. `2026.8.5.5168` does not sort correctly as a string and encodes a date that is not
meaningful for ordering.

**Ingest the version files.** Mirror the build list into the registry so the site knows every build that has
existed, its date, and its changelog. That gives:

- A dropdown of real builds instead of free text.
- "Compatible with 5117 to 5168" expressed as a range of real builds.
- A changelog diff between the user's build and the mod's tested build, which is a genuinely useful "why did
  this break" answer.

**Compatibility is author-declared and community-corroborated.** Let users report works or broken against a
specific build. Show declared range and reported results separately. Never auto-mark a mod broken from reports
alone, but surface a strong signal.

---

## 7. Conflict detection

KSA registers asset ids into one global table with `TryAdd` semantics, so a duplicate id from a later mod is
silently dropped with no error and no log line the user will find. Load order comes from `manifest.toml`
order.

Because the pipeline already extracts every asset id (§4 stage 6), the registry can do something no generic
mod site can:

**Cross-mod id collision detection.** Maintain a global index of `asset id -> versions that declare it`. On
publish, and on any mod page, show which other mods declare the same ids. Two mods that both define
`FuelTank` will not error in game, one will just vanish, and the user will have no idea why.

**Core override detection.** Any id that also exists in `Core` means the mod overrides stock content. Flag it,
show which ids, and mark the mod as requiring load-order placement before `Core`. This is a fragile category
and users deserve to be told before installing.

**Conflict surface on the install plan.** When a client resolves an install set, the API should return not
just dependency order but the id collisions within that set, so the client can warn before writing anything.

---

## 8. Dependency resolution

**In-game there is none.** `manifest.toml` is a flat ordered enable list. Everything must be resolved by the
client before writing.

The registry provides the graph, the client applies it:

- Declared dependencies as `id + version range`.
- Declared conflicts.
- Load order constraints: `before`, `after`, and the special `before Core`.
- An API endpoint that takes a desired set and a game build, and returns a resolved, ordered install plan or a
  clear explanation of why it is unsatisfiable.

Solve server-side and return the plan. Every client reimplementing a solver is how ecosystems get subtly
different resolution behaviour.

**Warn about manifest fragility.** The game rewrites `manifest.toml` whenever the user toggles mods in-game,
so any ordering the client established can be disturbed. Clients should re-check ordering on launch, and the
site should document this rather than pretending it is stable.

---

## 9. Discovery

**Browse and search:** full-text over name, description, author and readme. Facets for category, game build
compatibility, license, has-code vs content-only, and dependency count.

**Categories** should follow what people actually make, which for KSA means: parts, planets and systems,
visual, audio, gameplay and code, UI and HUD, tools, and translations.

**Sorting:** recently updated, most downloaded (see caveat below), newest, and a trending window.

**Download counts are unreliable and should be labelled as such.** The site does not serve the files. It can
count outbound clicks and API resolutions, which measures intent rather than installs. Either show it as
"resolutions" with an honest tooltip, or fetch real counts from the GitHub releases API where the artifact is
a GitHub asset. Do not present a click count as a download count.

**Collections and modpacks:** a named, versioned set of mod versions. Given that the game has no dependency
system, curated working sets are disproportionately valuable here. Treat a collection as a first-class object
with its own compatibility range.

---

## 10. Mod page

The page has to carry the honesty burden that hosting would otherwise carry.

- Identity: id, display name, author, license, links to source and issues.
- Versions, newest first, each showing compatible build range, download link, size, and `sha256`.
- **Availability status** per artifact: verified and when, unavailable, or quarantined.
- **Provenance:** GitHub App verified, or manually submitted URL. Say which, plainly.
- **Risk surface:** ships code yes or no, list of shipped assemblies, `[console]` commands verbatim if present.
- **Conflicts:** ids shared with other mods, ids overriding `Core`.
- **Validation report:** the warnings from §4, including undeclared content files.
- Readme, changelog, screenshots, and a dependency tree.

---

## 11. Public API

A mod manager is the primary consumer, and the site should assume one exists from day one.

```
GET  /api/v1/mods                       search and filter
GET  /api/v1/mods/{id}                  metadata, all versions
GET  /api/v1/mods/{id}/versions/{ver}   one version, artifacts, hashes, deps
POST /api/v1/resolve                    { mods[], gameBuild } -> ordered install plan
GET  /api/v1/builds                     known game builds from Content/Versions
GET  /api/v1/index.json                 full index snapshot, for offline clients
```

**Design rules:** unauthenticated reads, generous caching with ETags, a full-index snapshot so a client can
work offline and diff, and stable pagination. Every artifact in every response carries its `sha256`.

CKAN-KSA already exists in the community, so publishing an index format it can consume, or working with its
authors on a shared one, is likely worth more than a bespoke format.

---

## 12. Moderation and safety

**The threat model is unavoidable: code mods are unsandboxed .NET assemblies with full process privileges.**
Nothing in packaging fixes this. Say so plainly on every page that offers a code mod, rather than implying a
review makes it safe.

- Manual review queue for: first-time authors, any mod shipping a DLL, any `[console]` usage, and any tamper
  event.
- Author reputation tiers, so established authors publish without friction and new ones get a look.
- Report button on every mod, with categories for malware, stolen content, and broken.
- Takedown process, and a public moderation log for transparency.
- Because the site does not host, a takedown means delisting rather than deletion. Be explicit that delisting
  does not remove the file from GitHub or the author's server.

**Stolen content matters here.** The community norm is that decompiled game code is not published. A mod
shipping decompiled `KSA.dll` sources, or redistributing game assets wholesale, should be a reportable
category with a documented rule.

---

## 13. Author tooling

**Publish action.** A GitHub Action that builds, packs to the Archive Standard, validates locally with the
same rules the pipeline uses, and publishes on release. Authors should be able to fail their own CI on a
validation error rather than finding out at upload.

**Local validator CLI.** The pipeline's stages 3 to 8 as a standalone binary. Same code path as the server,
distributed as a single executable. This is what makes the Standard real rather than aspirational.

**Template repository.** A working mod skeleton with the correct `mod.toml`, the right compile-time references
marked so engine DLLs never ship, the packing script, and the action wired up.

**Migration helper.** Point it at an existing unstructured mod folder and have it emit a conforming archive
plus a suggested `mod.toml`, including declaring content files it found but that were never listed.

---

## 14. Known engine limitations to design around

Straight from the Archive Standard §13, restated as product constraints:

- **Craft cannot ship as mods.** `defaultvehicles` is hardcoded to `Content/Core/defaultvehicles`. If the site
  wants a craft-sharing category, it is a different install target and a different content type, not a mod.
  Worth raising with the developers as an engine ask.
- **New mods install disabled.** The client or the instructions must tell the user to enable it, or the client
  must write `manifest.toml` itself.
- **One shared user directory across installs.** Instancing is launcher-side. A client writing to the
  documents folder must resolve it the way the game does rather than hardcoding a path.
- **No in-game version awareness.** The user can always run any mod against any build. The site can warn, it
  cannot prevent.

---

## 15. Non-goals

Worth writing down so scope stays honest:

- Not a general file host.
- Not a sandbox. The site cannot make an arbitrary .NET assembly safe.
- Not a build service. Authors build their own artifacts.
- Not a forum. Link to Discord.
- Not a save or craft sharing site, at least not until §14's craft limitation is resolved, and then as a
  separate content type.

---

## 16. Phasing

**Phase 1, the index.** Auth, id claiming, manual URL submission, verification pipeline stages 1 to 5, mod
pages, search, read-only API. Enough to be useful and to start accumulating the id corpus.

**Phase 2, trust.** GitHub App provenance, periodic re-verification, tamper detection and quarantine,
moderation queue, reporting.

**Phase 3, resolution.** Dependencies, conflicts, asset id collision detection, the resolve endpoint, build
ingestion and compatibility ranges.

**Phase 4, ecosystem.** Publish action, local validator CLI, template repo, collections and modpacks, CKAN
interoperability.

Phases 1 and 2 are the product. Phase 3 is what makes it better than a spreadsheet. Phase 4 is what makes the
Archive Standard stick.

---

## 17. Open questions

1. Optional archival mirror for dead links: worth the hosting and licensing complexity, or accept link rot?
2. Interoperate with CKAN-KSA's index format, or define a new one and provide a bridge?
3. Are download counts worth showing at all given the site cannot measure them honestly?
4. Do collections need their own ids in the same namespace as mods, or a separate one?
5. What is the review threshold in practice: every DLL forever, or reputation-gated after the first?
