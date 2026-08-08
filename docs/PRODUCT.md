# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

**Primary: players who install mods for Kitten Space Agency.** Someone who owns the game, has a
specific build number, and wants to know whether a mod exists, whether it works with that build,
what it depends on, and whether it will collide with what they already have. They arrive from a
Discord link or a forums thread, usually already partway to installing something. When the three
audiences conflict, the player's job wins.

**Mod authors** are the supply side and the second audience. An author signs in with GitHub or
Discord, claims an id, connects a repository once, and thereafter just tags releases; the site
imports, validates and stamps them. Their reward for the friction is a validation report telling
them what is wrong with an archive before a user finds out the hard way.

**Client and tool developers** are the third audience. The public read API, the resolve endpoint and
the exported index exist so a mod manager can consume the catalogue without scraping. They are
served, not optimised for.

**Moderators** are a small internal audience with their own admin surfaces (review queue, reports,
listings, accounts, jobs, moderation log).

## Product Purpose

ksamods.gg is an **index for Kitten Space Agency mods, not a file host**. Archives live on the
author's own git forge; the site stores metadata, a pinned `sha256`, and a link. The validation
pipeline downloads an archive transiently, inspects it, and discards it — what persists is a URL, a
hash, and everything the validator learned from the bytes.

The product is **trust and resolution**, not files. It answers: does this mod exist, what version,
does it work with my game build, what does it depend on, is the file I just downloaded the file that
was reviewed, and will it conflict with the other mods I have.

Success is a player who installs a working set on the first try, and an author who learns about a
packaging mistake from their own CI or from the import report rather than from a bug thread.

## Positioning

**The site reads the archive the way the game reads it.** Nothing else in the KSA ecosystem does
this. RFC 0031's import reads `mod.toml` for identity, install root and dependencies; nothing in the
RFC corpus parses declared XML, checks that content paths resolve, or maintains a cross-mod asset id
index. That is genuinely additive and is the strongest argument for this project existing:

- **Two-hop reachability (pipeline stage 5).** Every path declared in `mod.toml` must resolve; every
  declared XML must parse with the right root element; every path referenced from inside that XML
  must resolve, transitively. Only files reachable from *neither* are warned about — a validator
  that warns on everything absent from `mod.toml` fires on the correct shape of every content mod,
  and a warning authors ignore is worth less than no warning at all.
- **Cross-mod asset id collision detection.** KSA registers asset ids into one global table with
  `TryAdd` semantics, so a duplicate id from a later mod is silently dropped with no error and no log
  line a user will find. The site maintains a global `asset id -> versions` index and can say which
  two mods will fight before either is installed.
- **Core override detection.** An id that also exists in `Core` means the mod overrides stock
  content and needs load-order placement before `Core`.

The difference this buys is between "installed fine, does nothing" and a useful error.

## Operating Context

- **The game is Kitten Space Agency** (Rocketwerkz). Grounded on build `2026.8.5.5168`, StarMap
  `0.4.6`. The running process is `StarMap.exe`, not the game.
- **KSA has no version negotiation at all** — no compatibility field, no minimum-version check. Any
  compatibility rule the site enforces is one the site invented, and it behaves accordingly: only
  *Incompatible* blocks; *Untested* and *Unknown* install after confirmation.
- **The loader has no failure surface.** A missing required dependency, a missing entry assembly, or
  a mod class without `[StarMapMod]` all end as a console line in a window most users never see.
  Every diagnostic the site surfaces at publish time is one the user would otherwise never get.
- **StarMap reorders initialisation, not installation.** `manifest.toml` is a flat ordered enable
  list; StarMap parks mods with unmet dependencies in a waiting graph and releases them across
  passes. Resolve order is what a client writes into `manifest.toml`, not a promise about init order.
- **The game rewrites `manifest.toml` every session**, and new mods install disabled with a relaunch
  needed to enable them. One shared user directory across installs, and two installs prune each
  other; instancing exists only through StarMap.
- **Craft cannot ship as mods** — `defaultvehicles` is hardcoded. Vehicles and saves are therefore
  separate content types with their own install target.
- Authors work on a git forge (GitHub at launch), tag a release, and expect the import to be
  unattended. Users work from a KSA forums thread, Discord, and a `mods/` folder on disk.

## Capabilities and Constraints

**Confirmed and built** (Phases 1–5 of `docs/backend.md` §18, plus a frontend, 247 tests passing):
listing creation and id claiming; the validation pipeline stages 3–8 as pure functions plus a
hardened container (`--network none`, read-only, no capabilities); the CLI `ksamods validate`
running the identical rules by construction; Postgres schema with an append-only moderation trigger;
live read endpoints, `POST /api/v1/resolve`, collisions; the RFC 0031 export and its public git
mirror; and the Blazor frontend rendering the full stack in both themes.

**Not yet built:** the release-import worker loop tying webhook → fetch → container → database (the
pieces exist and are tested separately), the periodic re-verification job, forge adapters beyond
GitHub, sign-in wired to real OAuth apps, and the modlist editor.

**Committed but not started: ksamods.gg will ship its own conforming client.** The plan's install
guarantee only holds if the normal way people install goes through a client that verifies the hash,
and this project will provide that client rather than deferring to Borea. Nothing on the site may
imply the client exists until it does.

**Hard product rules that future work must not break:**

- The site **never stores a mod file**. Not a mirror, not a cache, not "just for verified authors".
- **Releases are immutable and a version is stamped exactly once.** Re-appearing bytes under an
  existing version are refused, never absorbed. The way forward is a new version or a yank.
- **Post-publish amendments may only narrow** — yank with a reason, tighten a bound, add a missing
  dependency or conflict. Never widen, never remove, never touch identity, version, download or
  install data.
- **Three vocabularies stay separate and must never be collapsed:** the author's statement about a
  listing (`deprecated`, `superseded_by`), the author's statement about one build (`yanked`), and
  the index's statement about a listing (delisted, taken down, disputed — never author-writable).
- **Hash changes are classified, not uniformly quarantined.** A benign re-pack is flagged and the
  author asked to cut a real version; a changed DLL list, a new `[console]` entry or new asset ids
  is a tamper event that quarantines. A queue that is mostly false positives trains moderators to
  click accept.
- **The site cannot make an arbitrary .NET assembly safe.** Code mods are unsandboxed with full
  process privileges. Say so plainly wherever a code mod is offered; never imply review makes it
  safe.
- **A takedown is a delisting, not a deletion.** The file stays on GitHub or the author's server.
- **Download counts are not measurable honestly.** The site can count outbound clicks and API
  resolutions — intent, not installs. Label them as such or fetch real counts from the forge.
- Releases come from a git forge allowlist only, never arbitrary URLs.
- Ids are one global namespace across content types, case-insensitively unique, with mods holding
  priority over modlists in a dispute.
- **No third-party requests from any visitor page.** The privacy page promises no analytics, no
  third-party scripts, and one HttpOnly `SameSite=Lax` session cookie. Fonts and assets are
  self-hosted for that reason. This is a promise in shipped copy, not a preference.
- The browser only ever sees one origin; the frontend proxies `/api` and `/auth` to the API, which
  publishes no ports.
- Server-rendered by default so mod pages are indexable; interactive rendering only where a page
  needs it.
- Anonymous read is the default path — browsing and downloading need no account, and the site
  works read-only when no OAuth provider is configured.

**Terminology (fixed, from RFC 0031 / `docs/spec.md`):** Account, Mod, Release, Artifact, Modlist
(Draft and Version), listing, install root, `game_min` / `game_min_revision`, revision (the fourth
version component, which is what orders builds), dependency kinds `required` / `optional` /
`recommends` / `suggests` / `conflict`, `any_of`, loader, `mod-loader`, asset id, yank, deprecate,
supersede, quarantine, tamper event, benign divergence.

## Brand Commitments

- **Name:** ksamods.gg. A wordmark component already exists in the frontend.
- **Voice, evidenced across `README.md`, `docs/plan.md` and shipped page copy:** plain, technical,
  and willing to state its own limits in the same breath as its claims — "the site cannot guarantee
  a download exists", "download counts measure intent rather than installs", "not a sandbox". It
  explains *why* a decision was made rather than asserting it. It does not sell, does not hype, and
  does not soften a limitation into a feature. Future copy must be able to sit next to the privacy
  page without sounding like a different product wrote it.
- **Honesty is a load-bearing product commitment, not a tone.** The site does not host files, so
  every guarantee it offers is one it has to earn by saying exactly what it verified and when.
- Aligned with KSAModding RFCs 0017, 0025 and 0031, all Accepted. Interoperability with that format
  is a commitment, not a convenience.

## Evidence on Hand

- `docs/spec.md` — KSA Mod Archive Structure Standard v0.4. Every claim tagged `[CONFIRMED]`,
  `[CONVENTION]` or `[RFC]`, grounded in the shipped game assemblies.
- `docs/plan.md` — product plan v0.4, including explicit non-goals and open questions.
- `docs/backend.md` — architecture, schema, pipeline, security model, build order.
- `content-manager-design` submodule — the KSAModding RFC process (0017, 0025, 0031 Accepted).
- `db/seed-dev.sql` — development data that deliberately includes a yanked release, a quarantined
  artifact, a dead download link, a deprecated listing with a successor, and an asset id collision
  between two mods. **These are the states the UI most needs to get right**; a seed of nothing but
  healthy mods lets every one of them ship broken. Any surface work should be checked against it.
- 247 passing tests, including the container flag guard tests.
- Real validator output samples in `README.md` (KSAM-0503, KSAM-0801).

**Absences future work must not fabricate:** there are no users, no published mods, no download
figures, no testimonials, no press, no case studies, no pricing, no uptime record, and no launch
date. There is no screenshot library and no mod artwork the site owns — mod banners and screenshots
come from authors. There is no operator, funding source or SLA to claim.

## Product Principles

1. **Say what was verified, and when it was verified.** Not hosting the file means every guarantee
   is a claim about a moment in the past. A stale `last_verified` timestamp shown honestly is worth
   more than a green check that means nothing.
2. **Extract everything on first contact; defer only the surfacing.** The site does not keep the
   archive, so a fact not extracted at import may be unrecoverable once the asset URL rots.
3. **A warning nobody acts on is worse than no warning.** Reachability, not declaration. Classified
   divergence, not blanket quarantine. Precision is what keeps the signal alive.
4. **The unhappy states are the product.** Yanked, quarantined, unavailable, diverged, deprecated,
   incompatible, colliding — these are not edge cases to handle later. They are what distinguishes
   this from a list of links, and they must be designed first-class.
5. **One id, one namespace, one meaning.** A mod's id is the game's folder name; renaming breaks
   every install. Identity is frozen from first publication and succession is how continuity is
   expressed.
6. **Never blame the user for a metadata gap.** An unlisted dependency warns and proceeds; only true
   incompatibility blocks.

## Accessibility & Inclusion

**WCAG 2.2 AA is the bar this project holds itself to.** No legal obligation is claimed; it is the
standard future work is expected to meet. Both light and dark themes must meet it, and the mono-set
identifier strings (mod ids, versions, `sha256`, byte counts) must stay legible and unambiguous at
the sizes they are actually rendered at.

## Open Product Decisions

Recorded as undecided rather than assumed. Design work must not quietly settle them.

1. **RFC posture: peer implementation (posture 2), decided but not yet agreed with the KSAModding
   maintainers.** ksamods.gg implements RFC 0031 and 0017 exactly, runs its own listing, validation
   and index, and stays interoperable — two indexes, one format, records reconcilable by id. The
   direction is chosen and the codebase reflects it; the agreement is outstanding.
2. **Who operates and pays for the site, and what happens when they stop.** The git metadata mirror
   makes the catalogue survivable; accounts, modlist drafts and collaboration state are not mirrored
   and would be lost.
3. **Whether download counts are shown at all**, given the site cannot measure them honestly.
4. **The review threshold in practice:** every DLL forever, or reputation-gated after the first.
5. **Whether the validation data has a consumer** beyond this project's own planned client — asset
   id collisions, unreachable content files and unresolved declared paths are the distinctive
   contribution, and they are only worth producing if a client surfaces them.
6. **Whether vehicles and saves record which mods they need.** Open in RFC 0025 too.
7. **Legal exposure of linking rather than hosting** needs a lawyer's sentence before launch.
