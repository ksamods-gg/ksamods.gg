# ksamods.gg Feature Plan

**Status:** Draft v0.3
**Companion to:** [KSA Mod Archive Structure Standard v0.4](spec.md), [Backend Specification v0.1](backend.md)
**Grounded on:** KSA build `2026.8.5.5168`, StarMap `0.4.6`
**Aligned with:** KSAModding [RFC 0017](../content-manager-design/rfcs/0017-game-version-ordering-and-compatibility.md),
[RFC 0025](../content-manager-design/rfcs/0025-scope.md),
[RFC 0031](../content-manager-design/rfcs/0031-content-metadata-format.md), all Accepted

---

## 0. The decision this plan now hangs on

An earlier draft of this document designed ksamods.gg as a standalone index with its own metadata format, its
own API and its own resolver. That work was done without knowledge of the KSAModding
`content-manager-design` RFCs, three of which are **Accepted** and cover overlapping ground:

| RFC | What it settles |
|---|---|
| 0025 | Scope: one content manager for mods, packs, vehicles and saves. Its own index format. SpaceDock and other hosts are download locations, not metadata authorities. [Borea](https://github.com/KSAModding/Borea) is the client. |
| 0031 | The metadata format: authored TOML plus generated JSON, living in the index, never in the archive. Id rules, dependency kinds, release stamping, post-publish amendments. |
| 0017 | Version ordering by revision, and the four-state compatibility model. |

**Two competing indexes would be the worst outcome available.** The ecosystem is small enough that a split
metadata namespace means authors publishing twice, clients resolving differently, and neither index being
complete. Everything downstream of this — the format, the API, the resolver, the phasing — depends on which of
three postures ksamods.gg takes:

1. **Front end to the RFC index.** ksamods.gg consumes the KSAModding index as its source of truth and is the
   web presence, search and discovery layer over it. It contributes validation back upstream. This is the
   smallest surface and the fastest to something real, and it concedes format authority.
2. **Peer implementation of the same format.** ksamods.gg implements RFC 0031 and 0017 exactly, runs its own
   listing and validation, and stays interoperable. Two indexes, one format, records reconcilable by id.
   Costs duplicated effort and needs a sync story, but survives either project stalling.
3. **Separate registry with its own format.** What the earlier draft implied. Only defensible if the RFC
   process fails or the format turns out to be unworkable, and neither is in evidence — RFC 0031 is careful,
   well-grounded work.

**This document now assumes posture 1 or 2 and is written to be compatible with both.** Where a decision
differs between them it is called out. Posture 3 is not planned for; adopting it would need a specific reason
this plan does not currently have.

What does *not* change under any posture is the part of this plan the RFCs do not cover at all: **archive
content validation and asset id collision detection** (§4 and §7). RFC 0031's watcher reads `mod.toml` for
identity, install root and dependencies. Nothing in the RFC corpus parses declared XML, checks that content
paths resolve, or maintains a cross-mod asset id index. That is genuinely additive, it is the difference
between "installed fine, does nothing" and a useful error, and it is the strongest argument for this project
existing regardless of which posture wins.

---

## 1. What this site is

**ksamods.gg is an index, not a file host.** Archives live on GitHub releases or on the author's own server.
The site stores metadata, a pinned hash, and a link. RFC 0025 reaches the same conclusion and states it as a
non-goal: hosting content files, and anything needing a service somebody has to keep running and paying for.

**Note the second half of that constraint, because this plan does not currently satisfy it.** RFC 0025 puts
the index and its automation on free infrastructure — GitHub repositories and Actions — following the proven
self-updating `builds.json` pattern. This document assumes a hosted web service with a database, a moderation
queue and a live API. That is a running cost and an operator, indefinitely. Under posture 1 the durable data
lives in the RFC index and ksamods.gg is a cache and a front end, which keeps the failure mode to "the site is
down" rather than "the ecosystem's metadata is gone". Under posture 2 the site needs an explicit answer for
who pays and what happens when they stop. Either way it belongs in §17 as a real question, not an assumption.

That one decision drives most of this document, so it is worth being blunt about what it buys and what it
costs.

**What it buys:** no storage bill, no bandwidth bill, a smaller and simpler takedown surface, and authors keep
control of their own distribution. Note that not hosting *changes* the legal exposure rather than removing it:
linking still carries contributory-infringement risk, and declining to host also declines the safe-harbour
protections a host can claim. This needs a lawyer's sentence before launch, not a founder's.

**What it costs:** the site cannot guarantee a download exists, cannot guarantee the bytes at a URL today are
the bytes that were reviewed, and cannot fix a dead link. Every feature below that mentions **integrity**,
**availability** or **tamper detection** exists to buy back some of what hosting would have given for free.

**The site's actual product is trust and resolution**, not files. It answers: does this mod exist, what
version, does it work with my game build, what does it depend on, is the file I just downloaded the file that
was reviewed, and will it conflict with the other mods I have.

---

## 2. Domain model

```
Author        a person or org, owns Content
  └── Content   stable identity, one global id namespace across every type
        │       type ∈ { mod, modpack, mod-loader, (vehicle, save) }
        │       authored metadata: identity, links, license, compatibility, loader, dependencies
        └── Release   SemVer, immutable once stamped
              └── Artifact   one downloadable archive: url + sha256 + sizes
```

**Content, not just mods.** RFC 0025 scopes the ecosystem to mods, mod packs, vehicles and saves, with the
content type a required field from day one so that widening later is an extension rather than a break. This
plan adopts that. It also changes an earlier non-goal here: craft sharing is no longer out of scope pending an
engine fix, it is a separate content type with its own install target (§15).

`mod-loader` is a type too, so StarMap is listed, versioned and depended on like anything else, and a resolver
never has to special-case it.

**Id rules come from RFC 0031, via Standard §3.** ASCII, 1–64 characters, alphanumeric at both ends,
case-insensitive comparison with authored casing preserved, `Core` and Windows device names reserved and
checked up to the first dot. The earlier `<author>.<mod>` lowercase rule and the legacy-grandfathering clause
it made necessary are both withdrawn — RFC 0031's format admits `AdvancedFlightComputer` directly, so there is
nothing to grandfather.

**The namespace is global across types.** A mod, a pack and a loader can never share an id, which keeps every
reference type-free. The cost is first-come-first-served across types, and it is the right trade.

**Metadata splits by who writes it, and the split is the important part** (RFC 0031):

- **Authored, one TOML file per listing, written by a human, rarely.** Identity, links, license, compatibility
  bounds, loader requirement, dependencies. Facts that outlive any single release.
- **Generated, one JSON file per release, stamped by tooling, unattended.** Version, download URL, checksum,
  sizes, install root, resolved dependency list, and a snapshot of how the listing described itself at that
  moment.

This is CKAN's authored/generated split, and it earned its place: the one time a release file in the
CKAN-for-KSA index was hand-written, it was invalid JSON and shipped a wrong install path. Nobody hand-writes
a release file.

The `listing` snapshot is subtle and worth keeping: it lets a client show version 3 as it was described when
version 3 shipped, instead of advertising features that only arrived in version 4. But deprecation, succession
and index status are deliberately **not** snapshotted — those must reach every release the moment they are
declared, so a client always reads them live.

**Mod packs have no generated half.** A pack is pure reference metadata, one authored document per pack
version, pinning exact `(id, version)` pairs. There is no archive, so there is nothing to watch and nothing to
stamp. Exact pins rather than ranges, because a pack is a curated tested set rather than a statement of need.

**Releases are immutable, and a version is stamped exactly once.** If a host's tag for an already-stamped
version reappears with different bytes, the watcher **rejects it and never overwrites**. The author's way
forward is a new version, or a yank of the broken one. This is the only way an index without hosting can offer
an integrity guarantee at all, and it is stricter than the earlier draft here, which contemplated accepting
changed bytes as a new version after review.

**Artifacts may be multiple per release**, for example a GitHub asset plus a mirror, and any source whose
bytes match the recorded `sha256` is acceptable — which also lets clients fall back to caches. Mirrors are
stamped only when a non-authority host serves an archive with identical bytes.

---

## 3. Publishing

### 3.1 Claiming an id

1. Sign in with GitHub or Discord.
2. Request an id. Validated against the Standard §3 format rules, which are RFC 0031's.
3. Reserved: `Core`, anything colliding case-insensitively with an existing id of **any** content type, and
   Windows device names compared up to the id's first dot.
4. Supply a KSA forums thread. Required, per RFC 0031, and it does real work: it ties the listing to an Ahwoo
   account, it is the tiebreaker in an id dispute, and it is a takedown tripwire. It is also the cheapest
   ownership signal available to a project that cannot verify domains.
5. Claim is immediate for unused ids. Disputes go to moderation, with the forums thread as evidence.

**Case-insensitive reservation is intentional and narrower than the game.** KSA treats `MyMod` and `mymod` as
distinct, but they cannot both live in one `mods/` folder on Windows, so the registry reserves them as one.
Original casing is preserved for display and for the install path; only the uniqueness check is folded.

**Id squatting policy:** an id with no published version after 90 days can be reclaimed on request. This does
not conflict with the immutability rule in §2 — that rule binds from first publication, and an unpublished
claim is a reservation rather than an identity. Once a version exists, the id is frozen permanently.

### 3.2 Submitting a version

**The author writes one file, once, and then stops.** RFC 0031's shape: an authored TOML document declares a
`[releases]` section naming where releases appear — `github = "owner/repo"` or `spacedock = 4253` — and from
then on the author tags a release and goes to bed. A watcher fetches the archive, computes the checksum, reads
dependencies out of the archive's own `mod.toml`, stamps a release file, and the release is installable.

More than one host key is allowed, and then an `authority` key naming one of them is required: the authority
defines which releases exist and when, and other hosts are checked only for an archive with identical bytes,
which is how mirrors get populated.

The `[releases]` section is optional. Without it, releases enter by pull request, which stays the path for
content hosted where no watcher looks.

**Provenance still differs by path, and the page should say which.** A watched GitHub release carries repo,
tag, commit and asset id, so provenance is real. A pull-requested release or a pasted URL does not prove
control of anything, and §5 escalates accordingly.

**Validation happens at publish time, in front of the author.** A version string that does not parse as SemVer
rejects the release then and there, rather than surfacing later in front of users. This is CKAN's most
transferable operational lesson: a malformed template failed inflation with the error in front of the person
who could fix it.

The pipeline (§4) runs on every path. No path stores the archive permanently.

**Authored metadata is fixed by editing the index, not by re-releasing.** A corrected link, a tightened
compatibility bound, a newly-learned dependency: all index edits. This is a direct consequence of metadata not
living in the archive (Standard §5.3), and it is one of the strongest reasons for that choice.

### 3.3 Version metadata

Split by who writes it, per RFC 0031. The left column is the authored file; the right is stamped per release.

| Authored, once | Generated, per release |
|---|---|
| `id`, `type`, `name`, `authors`, `abstract`, `license` (SPDX) | `version`, normalised to SemVer 2.0.0, leading `v` stripped |
| `description`, `tags`, `status`, `superseded_by` | `release_status`: `stable`, `testing` or `dev`, derived from host flags and the pre-release part |
| `[links]` with `forums` required | `release_date`, ISO 8601 UTC |
| `[releases]` host and authority | `download`: `url`, `sha256`, `size`, `content_type`, optional `mirrors` |
| `[compatibility]`: `game_min` required, `game_max` and `os` optional | `game_min` plus resolved `game_min_revision` (§6) |
| `[loader]`: `id`, `min` required, `max` optional | `install.root` and whether it was derived |
| `[[dependencies]]`: `id`, `kind`, optional `min`/`max` | merged `dependencies`, each tagged `authored` or `derived` |
| | `install_size`, `changelog`, `listing` snapshot |

Two fields carry more weight than their size suggests:

**`status` and `superseded_by`.** A renamed mod is unavoidably a different mod, because the id is the folder
name. Without a successor pointer, every rename and every continuation fork strands its users on a dead id.
Succession is deliberately not a dependency — "I am replaced by X" does not mean "I need X", and putting it in
the dependency list would make every resolver filter it back out.

**`status` is the author's voice only.** Delisted, taken down and disputed are statements the *index* makes
about a listing, and must not be author-writable. Keep the two vocabularies separate from the start; merging
them is very hard to undo.

**Dependency kinds** follow Debian's precedent: `required`, `optional`, `recommends`, `suggests`, `conflict`.
An entry may carry `any_of` instead of `id` — an array of alternatives, satisfied by any one — valid with
`required` or `recommends`.

**Bounds are two typed inclusive fields, not a range expression.** No `>=0.1.0 <0.2.0` syntax. An expression
needs a parser whose edge cases this project would have to specify itself, and npm, cargo and NuGet all differ
subtly on exactly those edges. Two fields are schema-validatable and map one-to-one onto what a resolver
evaluates.

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

**Stage 4, structure and install root.** Standard §2 and §12: exactly one top-level directory, name equal to
the claimed Id compared case-insensitively, `mod.toml` present and parsing. Stamp `install.root` with
`derived: true` when that standard shape is found. A name mismatch is an error, not a warning — the folder
name is the identity the game will see, and no amount of correct metadata survives getting it wrong.

**Stage 5, declaration integrity.** This is the check nobody else can do for KSA, and it is the most valuable
thing in the pipeline.

- Resolve every path declared under `assets`, `systems`, `fonts`, `starBinaries`, `planetMeshes`,
  `planetRandomHeightmapCollections`. A declared path that does not exist is an **error**.
- Parse every declared XML. Malformed XML is an **error**. Wrong root element is an **error**.
- Resolve paths referenced *from inside* that XML — meshes, textures, shaders, sounds — transitively. A
  referenced path that does not exist is an **error**.
- List content files reachable from neither `mod.toml` nor any declared XML. This is a **warning**.

**Test reachability, not declaration.** Only the six list keys load directly from `mod.toml`; textures and
meshes are correctly absent from it and referenced by path from the XML instead (Standard §7, and the example
in Standard §14). Warning on everything not named in `mod.toml` would fire on the normal shape of every
content mod, and the warning authors ignore is worth less than no warning at all. The two-hop reachability set
is small and every miss in it is a genuine wiring mistake.

**Stage 6, asset id extraction.** Walk the parsed XML, collect every `Id` attribute, and store it against the
version. Feeds conflict detection (§7).

**Run stage 6 from day one, even though §7 ships in Phase 3.** The walk is nearly free — stage 5 already holds
the parsed tree — and the input is perishable. The site does not host artifacts, so backfilling this later
means re-fetching every published version, by which point some of those URLs are exactly the dead links §5
predicts. The oldest mods would be permanently missing from the collision index. The general rule: extract
everything the site will ever want from an archive on first contact, and defer only the surfacing.

**Stage 7, code facet.** If a DLL is present: confirm the entry assembly resolves — `[StarMap].EntryAssembly`
when set, otherwise `<Id>.dll`, which is the loader's documented default and the reason `AircraftHUD` works
with no `[StarMap]` block at all. Reject game and engine assemblies (`KSA.dll`, `Brutal.*`, `Planet.*`), warn
on loader assemblies with a pointer at the `StarMap.API` NuGet reference as the fix, and list every shipped
assembly on the mod page.

Do **not** warn about third-party DLLs duplicated across mods. Each mod loads in its own
`ModAssemblyLoadContext`, so two mods shipping different versions of the same library is supported by design
rather than a conflict (Standard §6).

**Stage 7b, dependency extraction.** Read `[[StarMap.ModDependencies]]` — `ModId`, `Optional`,
`ImportedAssemblies` — and record each entry as `derived`. This is the only machine-readable dependency data
that exists anywhere in the KSA ecosystem, and the loader acts on it at runtime, so it is ground truth rather
than a hint. Merge with authored entries per RFC 0031: an authored entry replaces the derived entry with the
same id and may add the version bounds the loader cannot express, but **a derived entry can never be
suppressed**, because the loader will act on it whatever the index says.

One validation rule that is easy to miss: an authored `any_of` may only name members whose derived entries
carried `Optional = true`. Otherwise the loader refuses to start the mod without that specific dependency, and
the index would be advertising a choice that does not exist at runtime.

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
- **Changed hash:** re-run the full pipeline against the new bytes and classify before escalating (below).

**A version is stamped exactly once, and changed bytes never overwrite it.** This is RFC 0031's rule and it is
the right one. A re-appearing tag with different bytes is refused, not absorbed. The author's way forward is a
new version, or a yank of the broken one. An earlier draft here contemplated a moderator accepting changed
bytes as a new version; that is withdrawn, because it makes the stamp mean less than it should.

**Classify hash changes, do not quarantine them all.** Authors re-upload release assets routinely: a bad
build, a CI re-run, a fix to the packing step. Those will be the overwhelming majority of hash-change events.
A queue that is mostly false positives trains moderators to click accept, which destroys the signal precisely
when a real compromise arrives. Two axes decide severity:

**What changed.** Re-run stages 3 to 8 on the new bytes and diff the result against the stored one. A re-pack
— same asset ids, same shipped assemblies, same `[console]` arrays, no new file types — is a **benign
divergence**: flag it on the mod page, notify the author, ask them to cut a real version. A changed DLL list,
a new `[console]` entry, or newly-declared asset ids is a **tamper event**: quarantine immediately, hide the
download, notify author and moderators, require a human. In neither case does the stamped record change.

**Where it came from.** A watched GitHub release knows whether it was edited, when, and by whom, which
corroborates a benign re-upload. A pasted URL or a pull-requested release has no such signal, so an
unexplained change there escalates one level: benign divergence becomes a review item, and a tamper event
reaches the author's other listings as well.

**Yanking is the author's own tool and is not a tamper event.** A yank retracts one build: it stays in
history, clients stop offering it for new installs and updates, an already-installed copy is left alone and
shows the reason. It is distinct from `deprecated`, which covers the whole listing, and from an index-side
takedown, which is not the author's voice at all. Keep all three separate.

**Post-publish amendments exist, and they may only ever narrow.** RFC 0031's invariant is worth stating in
full because it is the thing that keeps immutability meaningful while still allowing knowledge gained after
release to be recorded:

- Allowed: setting `yanked` with a reason; tightening game compatibility by adding or lowering `game_max` or
  raising `game_min`; tightening a dependency or loader bound; adding a dependency or conflict entry that was
  missing.
- Never allowed: widening or removing a bound, removing an entry, or touching identity, version, download or
  install data.

The reasoning: a release that turns out to support *more* than it was stamped with keeps its stamp, because
nobody re-verified the wider claim against the actual archive. Narrowing records something learned; widening
asserts something unchecked.

This also means a release that breaks only above a certain game build gets its compatibility tightened rather
than yanked, so it stays installable where it still works. That is a better outcome than the binary the
earlier draft had.

Because amendments live only in the index, **a cached release file cannot show a yank**. A client must refresh
its index data before offering installs or updates. Document that as a hard requirement.

**Client-side enforcement.** The API always returns the pinned hash. A client that does not verify is
non-conforming. Document this as a hard requirement, not a suggestion.

**The delivery risk is real but it now has an owner.** Hash pinning, resolve plans, load-order constraints and
conflict warnings all reach the user through a client. The path of least resistance — open the page, click the
link, drag the folder into `mods/` — verifies nothing, and it is what most users will do. An earlier draft
called this the plan's load-bearing unknown. RFC 0025 answers it: [Borea](https://github.com/KSAModding/Borea)
is the client, in the KSAModding org, with the specification kept implementation-neutral so others can follow.

What remains is narrower. A client that does not verify is non-conforming, and saying so is not enough on its
own — the guarantee only holds if the normal way people install goes through a conforming client. That is a
distribution question rather than an engineering one, and it argues for pointing at Borea prominently on every
page rather than presenting the raw download link as the primary action.

**Optional archival mirror.** Even without being a general host, consider storing a copy of verified artifacts
under a permissive-license opt-in, purely as a fallback for dead links. This is the difference between a dead
mod and a lost mod. Make it author-opt-in at upload so the licensing question is answered up front.

---

## 6. Compatibility model

**This section follows RFC 0017, which is Accepted.** Standard §16 carries the full model and the evidence
behind it. What matters at product level:

**KSA has no version negotiation at all.** No compatibility field, no minimum-version check, nothing. Any
compatibility rule the site enforces is one the site invented, and it should behave accordingly.

**Order by the revision alone** — the fourth component. Not the full dotted string, which misorders 21
adjacent pairs across the shipped history, because the third component is a machine-local build counter that
*decreases* 32 times as the revision rises. The game's own `VersionInfo.CompareTo` does the same thing.

**Authors write a version, tooling stores a revision.** An author writes `2026.8.3.5117`, or a month like
`2026.7` meaning that whole calendar month. Publishing resolves it to a revision and stamps both. This is what
lets a client answer "is this compatible" offline, and for a release the index has not caught up with. Month
and explicit revision are the only two granularities offered; `Year.Month.Build` as a prefix is noise, since
19 of the 20 combinations that match multiple releases select a set that is not contiguous in time.

**Four states, and only one of them blocks:**

| State | When | Behaviour |
|---|---|---|
| Compatible | within the stated range | installs normally |
| Untested | newer than the stated upper bound | installs after confirmation |
| Incompatible | older than the lower bound | **blocked**, showing what it needs |
| Unknown | no usable bound at all | listed, installable after confirmation |

**A lower bound is required; an open upper bound is the recommended default.** At roughly thirteen releases a
month, any model requiring authors to re-state compatibility per release is stale within days. An open upper
end is an acknowledged false promise — a mod will eventually break silently and the site will have called it
maybe-compatible — but the alternative is worse, and Untested plus a confirmation is the honest middle.

**Do not build a build list; consume the one that exists.**
[`builds.json`](https://raw.githubusercontent.com/KSAModding/KSA-CKAN-meta/main/builds.json) in
KSAModding/KSA-CKAN-meta is updated hourly by a GitHub Action polling the game's own master server, has kept
itself current since 2026-07-02 with no human attention, and costs nothing to consume. Embed a copy as an
offline fallback, refreshed at release time, which is what the CKAN client does.

That gives the same three things the earlier draft wanted from ingesting `Content/Versions` directly — a
dropdown of real builds, ranges expressed in real builds, and a changelog diff between the user's build and
the mod's tested build as a genuinely useful "why did this break" answer — without operating anything.

**Compatibility is author-declared and community-corroborated.** Let users report works or broken against a
specific build. Show declared range and reported results separately. Never auto-mark a mod broken from reports
alone, but surface a strong signal — and route a confirmed break into a compatibility-tightening amendment
(§5) rather than a yank.

**Specialist builds are a known gap.** The developers ship builds for hardware vendors, agencies and
universities, and the same revision compiled development, release or production behaves differently. A user on
one gets answers computed as if they were on the public production stream. Name the limit on the page; do not
pretend to solve it.

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

**The game has none. StarMap has a partial one, and the difference matters.** `manifest.toml` is a flat
ordered enable list, but StarMap does not simply follow it: it walks the manifest once, initialises mods whose
dependencies are present, parks the rest in a waiting graph, and loops releasing waiters until a pass loads
nothing new. Anything still unmet never loads, and the only signal is a console line nobody reads.

So the loader reorders *initialisation* within the manifest walk. What it cannot do is versions, conflicts, or
telling the user anything at all. A client must not assume manifest order equals load order, and the registry
has to supply everything the loader lacks.

The registry provides the graph, the client applies it:

- Dependencies as `id` plus inclusive `min`/`max` bounds, with kinds `required`, `optional`, `recommends`,
  `suggests`, `conflict`, and `any_of` alternatives.
- The loader requirement as its own `[loader]` block, not a dependency entry — StarMap is content of type
  `mod-loader`, so it is listed and versioned like anything else, but a resolver never has to ask whether a
  given dependency is a loader.
- Load order constraints: `before`, `after`, and the special `before Core`.
- An endpoint taking a desired set and a game build, returning a resolved ordered install plan or a clear
  explanation of why it is unsatisfiable.

**Version bounds exist only here.** StarMap's dependencies are name-only by design, so "requires X 1.2 or
newer" is inexpressible at the loader level and unenforceable at runtime. The index is the only place it can
be said, and a client is the only thing that can act on it.

**An unlisted dependency warns and proceeds.** A dependency or pack member whose id is not in the index points
at wherever the author says it lives and lets the user continue. Blocking on a metadata gap punishes the user
for someone else's missing listing.

**Ship the solver as a library, and run that same library behind the endpoint.** Solving server-side is the
right default — every client reimplementing a solver is how ecosystems get subtly different resolution
behaviour — but the offline index snapshot in §11 means clients must be able to solve locally too, so a
server-only solver just guarantees a second implementation appears anyway. One library, two call sites, same
answer online and offline. This is the same shared-code-path argument as the validator CLI in §13.

**Warn about manifest fragility.** The game rewrites `manifest.toml` whenever the user toggles mods in-game,
so any ordering the client established can be disturbed. Clients should re-check ordering on launch, and the
site should document this rather than pretending it is stable.

---

## 9. Discovery

**Browse and search:** full-text over name, abstract, description, authors and readme. Facets for content
type, category, game build compatibility, license, has-code vs content-only, loader requirement, and
dependency count.

**Content type is the first facet**, since RFC 0025 puts mods, packs, vehicles and saves in one catalogue.
Default the browse view to mods so the widening does not make the common case worse.

**Categories** should follow what people actually make, which for KSA means: parts, planets and systems,
visual, audio, gameplay and code, UI and HUD, tools, and translations. RFC 0031 keeps `tags` free-form and
lowercase so a curated vocabulary can arrive later without a format change; start free-form, curate once there
is enough corpus to see the real shape.

**Sorting:** recently updated, most downloaded (see caveat below), newest, and a trending window.

**Download counts are unreliable and should be labelled as such.** The site does not serve the files. It can
count outbound clicks and API resolutions, which measures intent rather than installs. Either show it as
"resolutions" with an honest tooltip, or fetch real counts from the GitHub releases API where the artifact is
a GitHub asset. Do not present a click count as a download count.

**Mod packs are a content type, not a site feature.** RFC 0031 defines them: one authored TOML document per
pack version, self-contained, pinning exact `(id, version)` pairs for mods and — with the same entry shape —
vehicles and saves. No download, no checksum, no install data, no `[loader]` block (it follows from the pinned
mods), and no nested packs in `spec_version = 1`. A pack never redistributes anyone's files; each member
downloads from its own host.

Given the game has no dependency system, curated working sets are disproportionately valuable here, and exact
pins are the point: a dependency says "any X in this range works", a pack says "this exact set is what I
curated and tested". The two are not in tension.

Note the vehicle and save sections are already defined in `spec_version = 1` even though those content types
have not landed. That costs nothing now and saves a format bump later — worth copying as a habit.

---

## 10. Listing page

The page has to carry the honesty burden that hosting would otherwise carry.

- Identity: id, type, display name, authors, license, links to source and issues, and the forums thread.
- Releases, newest first, each showing compatible build range, download link, sizes, and `sha256`.
- **Compatibility state against the visitor's build** where known: Compatible, Untested, Incompatible or
  Unknown (§6), rendered in full version strings rather than raw revisions.
- **Loader requirement:** which loader and which version bound, or "runs without a loader" for asset-only mods.
- **Availability status** per artifact: verified and when, unavailable, diverged, or quarantined.
- **Provenance:** watched release with repo, tag and commit, or a submitted URL. Say which, plainly.
- **Risk surface:** ships code yes or no, list of shipped assemblies, `[console]` commands verbatim if present.
- **Conflicts:** ids shared with other mods, ids overriding `Core`.
- **Validation report:** the warnings from §4, including content files unreachable from `mod.toml` and XML.
- **Deprecation and succession** where declared, read live rather than from a release snapshot, pointing at
  `superseded_by` — this is what stops a rename stranding its users on a dead id.
- **Yanked releases** shown with their reason, still in history, not offered for install.
- Readme, changelog, screenshots, and a dependency tree.

**Render a release as it described itself when it shipped.** RFC 0031's `listing` snapshot exists so browsing
version 3 does not advertise what only arrived in version 4. But status, succession and index-side state
always come from live data — a deprecation has to reach every release the moment it is declared. The snapshot
is display history and is not amendable: a typo in an old stamp stays, and the correction lands in the
authored file where it fixes the current view and every future stamp.

---

## 11. Public API

A mod manager is the primary consumer, and the site should assume one exists from day one.

```
GET  /api/v1/mods                       search and filter
GET  /api/v1/mods/{id}                  metadata, all versions
GET  /api/v1/mods/{id}/versions/{ver}   one version, artifacts, hashes, deps
POST /api/v1/resolve                    { content[], gameBuild } -> ordered install plan
GET  /api/v1/builds                     known game builds, mirroring builds.json
GET  /api/v1/index.json                 full index snapshot, for offline clients
```

Read `/mods` as `/content` once vehicles and saves land; the type is a field, not a route.

**Design rules:** unauthenticated reads, generous caching with ETags, a full-index snapshot so a client can
work offline and diff, and stable pagination. Every artifact in every response carries its `sha256`.

**Serve RFC 0031 documents, do not invent a third format.** Under posture 1 the API is a query and search
layer over the KSAModding index; under posture 2 it serves the same authored-TOML and generated-JSON shapes
from its own store. Either way `spec_version` is stamped on every document, and a client that meets a
`spec_version` it does not implement renders the entry as unknown rather than silently dropping it.

Format evolution follows RFC 0031: adding an optional field is not a break and does not bump the integer;
removing a field, changing a meaning, or adding a required field does. New content types extend the `type`
enum without a bump, because a client that does not know a type already treats it as unknown.

**On CKAN.** CKAN-KSA is a real, working field study — a full fork with KSA support, running against a live
index since July 2026, with every decision recorded in public issues. It is the best evidence this ecosystem
has about what the game forces on a manager, and §13 and §5 above both lean on it.

It is not, however, the interop target. Its upstream pull request has sat without maintainer review since
2026-07-06, and everything downstream of that merge — the metadata tester, the indexer bot, the status page —
gates on somebody else's calendar. RFC 0025 chose to define its own index and self-host automation on GitHub
Actions precisely to avoid inheriting that choke point, and that reasoning holds. Take the versioning model,
which is proven in production; skip the build-counter normalisation, which was CKAN's constraint and not this
project's. The metadata side of CKAN-KSA stays consumable by anyone with no permission needed, so a bridge is
cheap if it is ever wanted.

---

## 12. Moderation and safety

**The threat model is unavoidable: code mods are unsandboxed .NET assemblies with full process privileges.**
Nothing in packaging fixes this. Say so plainly on every page that offers a code mod, rather than implying a
review makes it safe.

- Manual review queue for: first-time authors, any mod shipping a DLL, any `[console]` usage, and any tamper
  event.
- Author reputation tiers, so established authors publish without friction and new ones get a look.
- Report button on every listing, with categories for malware, stolen content, and broken.
- Takedown process, and a public moderation log for transparency.
- Because the site does not host, a takedown means delisting rather than deletion. Be explicit that delisting
  does not remove the file from GitHub or the author's server.

**The required forums thread is the moderation hook, and it is doing more work than it looks like.** It ties
every listing to an Ahwoo account, which is the only identity signal available to a project that cannot verify
domains; it is the tiebreaker when two people claim one id; and it is a takedown tripwire, since a thread
disappearing is a signal worth acting on. Read it live from the authored file rather than from any release
snapshot, so moderation always sees the current value.

**Keep the author's voice and the index's voice strictly separate.** `status = "deprecated"` and
`superseded_by` are the author's statement about their own content. Delisted, taken down and disputed are the
index's statements about a listing, and must never be author-writable. A yank sits between them: the author's
statement about one specific build. Three distinct vocabularies, and collapsing any two is very hard to undo
once published data exists.

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

**Template repository.** A working mod skeleton with the correct `mod.toml`, a `StarMap.API` PackageReference
with `Private="false"` so the loader assembly never lands in output, game and engine references marked so they
never ship, the packing script, and the action wired up. Getting the reference flags right in the template is
worth more than any number of validator warnings about them later.

**Migration helper.** Point it at an existing unstructured mod folder and have it emit a conforming archive
plus a suggested `mod.toml`, including declaring content files it found but that were never listed.

**Authored-file scaffolding.** Generate the RFC 0031 authored TOML from what can be inferred — id from the
folder name, `[loader]` from the presence of a DLL, `[[dependencies]]` from `[[StarMap.ModDependencies]]`,
`license` from a detected `LICENSE` file — and leave the author to fill in `abstract`, the forums link and
`game_min`. The authored file is written once and rarely touched, which makes the first write the moment where
tooling pays for itself.

---

## 14. Known engine limitations to design around

Straight from the Archive Standard §13 and §16, restated as product constraints:

- **Craft cannot ship as mods.** `defaultvehicles` is hardcoded to `Content/Core/defaultvehicles`. Vehicles
  and saves are therefore separate content types with their own install target, which is exactly what RFC 0025
  does. No longer blocking anything; still worth raising with the developers as an engine ask.
- **New mods install disabled, and enabling needs a relaunch.** The client must write `manifest.toml` itself
  or the user gets a mod that appears to install and does nothing. StarMap already handles the relaunch: it
  detects newly enabled mods, offers a confirm dialog, and respawns itself, doing file operations in the new
  process before mods load. The client's contract is write files, then launch.
- **The manifest is the game's, and the game rewrites it every session.** A client writes `enabled = true` for
  what it installs, never flips an entry it did not create, removes only what it manages, and keeps its own
  state in a sidecar because the manifest cannot carry foreign keys. All four are the CKAN fork's hard-won
  answers, not theory.
- **One shared user directory across installs, and two installs prune each other.** A mod in install A's
  `Content/` loses its manifest entry the next time install B launches. The game offers no override at all: no
  useful CLI flag, and it reads no environment variables anywhere in the shipped assemblies. **Instancing
  exists only through StarMap**, via `-InstancePath` or `STARMAP_INSTANCE_PATH`, and it moves an entire
  profile rather than just the mods. A client can drive it per launch without touching StarMap's config.
- **The running process is `StarMap.exe`, not the game.** Anything detecting "is the game running" by process
  name needs to know that, and mods are loaded before the game's own entry point runs.
- **No in-game version awareness.** The user can always run any mod against any build. The site can warn, it
  cannot prevent — which is the whole reason only Incompatible blocks (§6).
- **The loader has no failure surface.** A missing required dependency, a missing entry assembly, or a mod
  class without `[StarMapMod]` all end as a console line in a window most users never see. Every diagnostic
  the site surfaces at publish time is one the user would otherwise never get at all.

---

## 15. Non-goals

Worth writing down so scope stays honest:

- Not a general file host.
- Not a sandbox. The site cannot make an arbitrary .NET assembly safe.
- Not a build service. Authors build their own artifacts.
- Not a forum. Link to Discord, and to the required KSA forums thread.
- **Not a loader.** RFC 0025 is explicit and correct: StarMap is an external dependency the manager installs
  and depends on. Rebuilding the only working loader would split the ecosystem.
- Not a second metadata format. See §0.

**Removed from this list:** craft and save sharing. It was previously out of scope pending an engine fix.
RFC 0025 puts vehicles and saves in scope as their own content types, implemented after mods and packs, which
needs no engine change — so the reason for the non-goal is gone.

---

## 16. Phasing

**Phase 0, posture.** Settle §0 with the KSAModding maintainers before building anything that assumes an
answer. This is days of conversation, not weeks of work, and every phase below is cheaper once it is decided.
Doing it after Phase 1 means rewriting Phase 1.

**Phase 1, the index.** Auth, id claiming, submission by watched host and by pull request, verification
pipeline stages 1 to 7b, listing pages, search, read-only API serving RFC 0031 documents. Enough to be useful
and to start accumulating the corpus.

Stages 6 and 7b run here even though nothing consumes them until Phase 3. Both are nearly free at ingest and
impossible to backfill cheaply once artifact URLs start rotting (§4). Store extracted asset ids, extracted
`[[StarMap.ModDependencies]]`, and the full validation report from day one; surface them later.

**Phase 2, trust.** Watched-release provenance, periodic re-verification, hash-change classification, yanks
and the narrow amendment set, moderation queue, reporting.

**Phase 3, resolution.** Dependency graph, conflicts, asset id collision detection surfaced from the Phase 1
data, the resolve endpoint and its shared solver library, compatibility ranges over `builds.json`.

**Phase 4, ecosystem.** Publish action, local validator CLI, template repo, mod packs, and the vehicle and
save content types once RFC 0025's second implementation phase defines them.

Phases 1 and 2 are the product. Phase 3 is what makes it better than a spreadsheet. Phase 4 is what makes the
Archive Standard stick.

**The client question is answered and no longer sits in Phase 2.** RFC 0025 names
[Borea](https://github.com/KSAModding/Borea), transferred into the KSAModding org, as the implementation, and
keeps the specification implementation-neutral so other clients can consume the same metadata. That removes
what this plan previously called its load-bearing unknown. What replaces it is narrower and more tractable:
making sure the validation data this site produces is something Borea can actually consume, which is a
conversation to have during Phase 0 rather than a bet to place later.

---

## 17. Open questions

### Answered by the RFCs since v0.2

| Was open | Answer |
|---|---|
| Who builds the conforming client? | **Borea**, in the KSAModding org, per RFC 0025. The spec stays implementation-neutral so others can too. |
| Interoperate with CKAN, or define a new format? | **Define our own**, per RFC 0025. Take CKAN's versioning model and authored/generated split; skip the build-counter workaround and the upstream-merge choke point (§11). |
| Do collections share the mod id namespace? | **Yes.** RFC 0031 makes the namespace global across all content types, so a reference to an id needs no type. |
| Do legacy ids get a deadline? | **Moot.** RFC 0031's id format admits `AdvancedFlightComputer` directly, so there is no legacy tier and nothing to sunset. |

### Still open

1. **Which posture (§0), and agreed with the KSAModding maintainers rather than assumed?** This is now the
   load-bearing question, and it is the same *kind* of question the client one was: everything downstream
   changes shape depending on the answer, and it gets more expensive to answer the longer building continues.
2. **Who operates and pays for the site, and what happens when they stop?** RFC 0025 explicitly avoids
   anything needing a service somebody keeps running. This plan assumes one. That tension is real and §1 names
   it; it needs an owner, not a paragraph.
3. Optional archival mirror for dead links: worth the hosting and licensing complexity, or accept link rot?
   Note this one now cuts against RFC 0025's no-hosting non-goal as well as costing money.
4. Are download counts worth showing at all given the site cannot measure them honestly?
5. What is the review threshold in practice: every DLL forever, or reputation-gated after the first?
6. **Does the validation data have a consumer?** Asset id collisions, unreachable content files and declared
   paths that do not resolve are this project's distinctive contribution (§0), but they are only worth
   producing if Borea or another client surfaces them. Worth confirming early, since it is the justification
   for a good deal of Phase 1.
7. **Do vehicles and saves record which mods they need?** Open in RFC 0025 too. It decides whether dependency
   resolution extends to the new content types or whether publishers declare by hand.
