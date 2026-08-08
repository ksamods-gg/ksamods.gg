# ksamods.gg Backend Specification

**Status:** Draft v0.3
**Companion to:** [KSA Mod Archive Structure Standard v0.4](spec.md), [ksamods.gg Feature Plan v0.3](plan.md)
**Grounded on:** KSA build `2026.8.5.5168`, StarMap `0.4.6`
**Follows:** KSAModding RFC 0017, RFC 0025, RFC 0031, all Accepted

---

## 0. What this is

The server side of ksamods.gg: a web application where people sign in, create and maintain mod listings,
publish releases by connecting a git repository, and build modlists alone or together.

**The site never stores a mod file.** A release points at a release asset on the author's own forge — GitHub
at launch, other forges by adapter (§5.6). The pipeline downloads that archive transiently to validate it and
discards it; the durable record is a URL, a `sha256`, and everything the validator learned from the bytes.

### 0.1 The five decisions everything follows from

| Decision | Choice |
|---|---|
| Store of record | **Postgres**, mirrored continuously to a public git repository (§12). |
| Identity | **GitHub OAuth** primary, Discord secondary. Forge app installation is the ownership proof (§5.2). |
| Release source | **Git forge releases only**, from an allowlist of supported forges. Repo connected once, releases imported automatically thereafter (§5). |
| Modlists | **Owner plus invited collaborators.** Mutable draft, immutable published versions (§6). |
| Validation | **Full pipeline**, each job in a locked-down Docker container the site operates (§7). |

### 0.2 What changed from v0.1, and why it matters

v0.1 made git the store of record and published through pull requests. That is a good design for a
metadata-repository project and the wrong one here: this is a product where users create content in a browser
and two people co-edit a modlist. Publishing-by-PR fights both.

The consequences worth naming, because they are real costs of the reversal, each with its mitigation:

- **Metadata would not survive the site.** Under git-as-record the ecosystem kept its data if ksamods.gg
  stopped being operated. This is why **the git mirror in §12 is required rather than optional**: it restores
  most of that property for very little work. A tarball behind a CDN dies with the CDN; a public git
  repository can be forked by anyone who cares, and forks happen before the outage rather than after.
- **Moderation history is now a table to build**, not `git log`. It has to be append-only and exported, or the
  audit trail is only as trustworthy as the interface that writes it. §13.4.
- **Publish-time review is no longer PR review.** It becomes an explicit queue (§13.2).

The mirror does not fully replace git-as-record — it is a follower, so a compromised or buggy writer can
publish bad state into it, where a PR-based flow would have caught that at review. What it does guarantee is
that the data outlives the service, which is the property that actually mattered.

### 0.3 Relationship to the RFCs

RFC 0017's version model and RFC 0031's metadata shapes are followed exactly — they are what makes the
exported index consumable by Borea and anyone else. Two deliberate divergences, both because this is a site
rather than a metadata repository:

1. **Ownership is proven by forge app installation, not a forums thread.** RFC 0031 requires `links.forums`
   partly as an ownership signal for an index with no other option. Installing an app on a repository requires
   admin on that repository, which is strictly stronger. The forums link stays as recommended metadata and is
   required on export.
2. **Modlists are drafts before they are versions.** RFC 0031's modpack is a self-contained published document
   with no notion of an unpublished state. A collaborative editor needs one. Published modlist versions
   serialise to exactly the RFC's shape; drafts are site-native and never exported.

The id namespace stays shared across mods and modlists as RFC 0031 requires, with a priority rule the RFC does
not need but a site does. §2.1.

---

## 1. Architecture

```
  browser ──────▶ ┌──────────────────────────────────┐
                  │  KsaMods.Api   (ASP.NET Core)    │
  forge  ────────▶│   auth · mods · modlists · search│
  webhooks        │   enqueue jobs                   │
                  └───────────────┬──────────────────┘
                                  │
                  ┌───────────────▼──────────────────┐
                  │  Postgres        (RECORD)        │
                  │   users, mods, releases,         │
                  │   modlists, findings, moderation │
                  │   job queue (SKIP LOCKED)        │
                  └───────────────┬──────────────────┘
                                  │ claim job
                  ┌───────────────▼──────────────────┐
                  │  KsaMods.Worker                  │
                  │   1. fetch archive (SSRF-guarded)│
                  │   2. run container, no network   │◀── docker
                  │   3. persist findings + facts    │
                  │   4. discard the archive         │
                  └───────────────┬──────────────────┘
                                  │
                  ┌───────────────▼──────────────────┐
                  │  KsaMods.Exporter                │
                  │   static RFC 0031 index → CDN    │
                  │   optional git mirror            │
                  └──────────────────────────────────┘
```

### 1.1 Projects

| Project | Purpose |
|---|---|
| `KsaMods.Metadata` | RFC 0031 types, TOML/JSON serialisation, id rules, SemVer and KSA version types. No I/O. |
| `KsaMods.Validation` | The pipeline stages as pure functions over an archive. No network, no database. |
| `KsaMods.Resolver` | Dependency and conflict solving. Shared by the API and shipped as a library. |
| `KsaMods.Validator.Host` | The console app that runs *inside* the container. Reads an archive, writes a report. |
| `KsaMods.Worker` | Queue consumer. Fetches, orchestrates containers, persists results. |
| `KsaMods.Exporter` | Builds the static index (§12). |
| `KsaMods.Api` | ASP.NET Core. Everything user-facing. |
| `KsaMods.Cli` | `ksamods validate`, single-file AOT — the same rules authors can run locally. |

`KsaMods.Validation` doing no I/O is contractual, not stylistic. It is what makes the container, the CLI and
any future preflight run identical rules by construction rather than by discipline.

### 1.2 Library choices

- **TOML:** Tomlyn. StarMap parses `mod.toml` with Tomlet; §19 tracks whether that difference can bite.
- **Assembly inspection:** `System.Reflection.Metadata` with `MetadataLoadContext`. **`Assembly.Load` and
  `Assembly.LoadFrom` are banned** and enforced by an analyser (§14.4).
- **Archives:** `System.IO.Compression`, entry-by-entry through the guards in §14.2. Never
  `ExtractToDirectory` on untrusted input.
- **XML:** `XmlReader` with hardened settings (§14.3). Never `XmlDocument.Load`.
- **Queue:** Postgres with `FOR UPDATE SKIP LOCKED`. At this scale a dedicated broker is a component to
  operate for no benefit.

---

## 2. Domain model

```
Account
  ├── owns / maintains ──▶ Mod            id == the KSA folder name, globally unique
  │                          └── Release  one per forge release, immutable once imported
  │                                └── Artifact   forge asset URL + sha256 + sizes
  │
  └── owns / collaborates ──▶ Modlist
                                ├── Draft          mutable working set
                                └── Version        immutable snapshot, pins exact (mod, version)
```

**`Mod.Id` is the KSA folder name.** Not a slug. RFC 0031's rules apply (spec §3): ASCII, 1–64 characters,
alphanumeric at both ends, compared case-insensitively with authored casing preserved, `Core` and Windows
device names reserved and checked up to the first dot.

### 2.1 One namespace, but mods have priority in it

RFC 0031 makes the id namespace type-free so a reference to an id never needs a type alongside it. This spec
keeps that — a shared namespace across mods and modlists — because diverging would break the export.

Plain first-come-first-served in a shared namespace is wrong here, though, and the reason is an asymmetry the
RFC has no need to model:

**A mod's id is dictated by the game. A modlist's id is a free choice.** The folder name *is* the mod's
identity (`Mod.MakeUsing` overwrites anything else, spec §1), so a mod author who finds their id taken has no
alternative — they cannot pick another one and still ship a working mod. A modlist author in the same position
picks a different name and loses nothing but a preference.

So the rule is:

- Creating a modlist checks the id against **both** mods and modlists, as before. A taken id is refused.
- If a mod author needs an id held by a **modlist**, it is a dispute (§5.4) and **the mod wins**. The modlist
  is renamed, and its old id becomes a permanent alias redirecting to the new one.
- If a mod author needs an id held by another **mod**, ordinary dispute resolution applies and neither party
  gets an automatic win — both are equally constrained.
- The creation form says this plainly when a modlist id looks like a plausible mod folder name. Surprising
  someone at rename time is worse than warning them at creation time.

**Renaming a modlist is survivable; renaming a mod is not.** A modlist id appears in links and in the export;
an alias row covers both. A mod id appears in every user's `mods/` directory, in their `manifest.toml`, and in
every modlist that pins it — renaming breaks all of them at once. Giving the recoverable case priority over
the unrecoverable one is the whole argument.

**Releases are immutable once imported.** The version, the artifact URL and the hash are frozen. A forge
release re-uploaded with different bytes does not overwrite the record — it raises a divergence (§11.1). Only
the narrow amendment set in §5.5 may change a published release, and only in the narrowing direction.

**Modlist versions are immutable; the draft is not.** Publishing snapshots the draft into a new version and
leaves the draft available for further editing.

---

## 3. Schema

```sql
-- ─────────── identity ───────────
create table account (
  id             bigserial primary key,
  handle         citext not null unique,       -- site handle, user-chosen
  display_name   text not null,
  github_user_id bigint unique,
  github_login   text,
  discord_id     text unique,
  forums_url     text,                         -- recommended; required to export (§12)
  avatar_url     text,
  site_role      text not null default 'user', -- user | moderator | admin
  created_at     timestamptz not null default now(),
  suspended_at   timestamptz
);

create table oauth_identity (
  account_id  bigint not null references account(id) on delete cascade,
  provider    text not null,                   -- github | discord
  subject     text not null,
  linked_at   timestamptz not null default now(),
  primary key (provider, subject)
);

create table session (
  id           uuid primary key,
  account_id   bigint not null references account(id) on delete cascade,
  issued_at    timestamptz not null default now(),
  expires_at   timestamptz not null,
  revoked_at   timestamptz,
  user_agent   text,
  ip_hash      bytea                           -- hashed, not stored raw
);

-- ─────────── mods ───────────
create table mod (
  id             text primary key,             -- canonical casing
  id_lower       citext not null unique,
  type           text not null default 'mod',  -- mod | mod-loader
  name           text not null,
  abstract       text not null,
  description    text,                         -- CommonMark
  license        text not null,                -- SPDX expression
  tags           text[] not null default '{}',
  links          jsonb not null default '{}',
  status         text not null default 'active',   -- active | deprecated
  superseded_by  text references mod(id),
  listing_state  text not null default 'listed',   -- listed | unlisted | delisted | taken_down
  os             text[],
  created_by     bigint not null references account(id),
  created_at     timestamptz not null default now(),
  updated_at     timestamptz not null default now(),
  check (id_lower = lower(id))
);

create table mod_maintainer (
  mod_id     text not null references mod(id) on delete cascade,
  account_id bigint not null references account(id) on delete cascade,
  role       text not null,                    -- owner | maintainer
  added_by   bigint references account(id),
  added_at   timestamptz not null default now(),
  primary key (mod_id, account_id)
);

-- the connected repository; installation proves control (§5.2)
create table repo_link (
  mod_id           text primary key references mod(id) on delete cascade,
  provider         text not null,               -- github | gitlab | codeberg (§5.6)
  installation_id  text,                        -- provider-specific; null where not applicable
  repo_id          text not null,
  repo_full_name   text not null,
  linked_by        bigint not null references account(id),
  linked_at        timestamptz not null default now(),
  auto_import      boolean not null default true,
  last_seen_at     timestamptz,
  unique (provider, repo_id)
);

create table mod_release (
  id                 bigserial primary key,
  mod_id             text not null references mod(id) on delete cascade,
  version            text not null,            -- SemVer 2.0.0, normalised
  version_sort       bytea not null,           -- precomputed SemVer ordering key
  release_status     text not null,            -- stable | testing | dev
  released_at        timestamptz not null,
  provider           text not null,            -- forge this release came from
  provider_release_id text,
  provider_tag       text,
  provider_commit    text,
  changelog_url      text,
  changelog_body     text,
  game_min_revision  integer,                  -- null ⇒ Unknown (RFC 0017)
  game_max_revision  integer,
  loader_id          text references mod(id),
  loader_min         text,
  loader_max         text,
  install_root       text,
  install_derived    boolean,
  install_size       bigint,
  listing_snapshot   jsonb not null,           -- RFC 0031 `listing` block
  validation_state   text not null default 'pending',
                                               -- pending|running|passed|passed_warnings|failed
  availability       text not null default 'unverified',
                                               -- verified|unavailable|diverged|quarantined
  last_verified_at   timestamptz,
  yanked_at          timestamptz,
  yanked_reason      text,
  created_at         timestamptz not null default now(),
  unique (mod_id, version)
);
create index on mod_release (mod_id, version_sort desc);

create table release_artifact (
  release_id    bigint not null references mod_release(id) on delete cascade,
  url           text not null,                 -- forge release asset
  asset_id      bigint,
  sha256        bytea not null,
  size          bigint not null,
  content_type  text not null,
  primary key (release_id, url)
);

-- ─────────── what validation learned ───────────
create table release_asset_id (
  release_id  bigint not null references mod_release(id) on delete cascade,
  asset_id    text not null,
  xml_path    text not null
);
create index on release_asset_id (asset_id);

create table release_dependency (
  release_id   bigint not null references mod_release(id) on delete cascade,
  dep_id       text,
  group_id     integer,                        -- non-null groups any_of alternatives
  kind         text not null,                  -- required|optional|recommends|suggests|conflict
  min_version  text,
  max_version  text,
  source       text not null                   -- authored | derived
);

create table release_assembly (
  release_id     bigint not null references mod_release(id) on delete cascade,
  file_path      text not null,
  assembly_name  text,
  assembly_version text,
  is_entry       boolean not null default false
);

create table release_finding (
  release_id  bigint not null references mod_release(id) on delete cascade,
  stage       smallint not null,
  severity    text not null,                   -- error | warning | info
  code        text not null,                   -- stable, machine-readable
  message     text not null,
  path        text
);

create table release_console (
  release_id  bigint not null references mod_release(id) on delete cascade,
  hook        text not null,                   -- onBoot | onLoad
  ordinal     int not null,
  command     text not null
);

-- ─────────── modlists ───────────
create table modlist (
  id             text primary key,
  id_lower       citext not null unique,
  name           text not null,
  abstract       text not null,
  description    text,
  license        text not null default 'CC0-1.0',
  tags           text[] not null default '{}',
  links          jsonb not null default '{}',
  visibility     text not null default 'private',  -- private | unlisted | public
  listing_state  text not null default 'listed',
  draft_revision integer not null default 0,        -- optimistic concurrency (§6.3)
  created_by     bigint not null references account(id),
  created_at     timestamptz not null default now(),
  updated_at     timestamptz not null default now(),
  check (id_lower = lower(id))
);

-- retired modlist ids, kept forever so links and exports keep resolving (§2.1)
create table modlist_alias (
  alias_lower  citext primary key,
  modlist_id   text not null references modlist(id) on delete cascade,
  retired_at   timestamptz not null default now(),
  reason       text
);

create table modlist_collaborator (
  modlist_id  text not null references modlist(id) on delete cascade,
  account_id  bigint not null references account(id) on delete cascade,
  role        text not null,                   -- owner | admin | editor
  invited_by  bigint references account(id),
  accepted_at timestamptz,
  primary key (modlist_id, account_id)
);

create table modlist_invite (
  id          uuid primary key,
  modlist_id  text not null references modlist(id) on delete cascade,
  email_or_handle text not null,
  role        text not null,
  created_by  bigint not null references account(id),
  expires_at  timestamptz not null,
  accepted_by bigint references account(id)
);

-- the mutable working set
create table modlist_draft_entry (
  modlist_id   text not null references modlist(id) on delete cascade,
  entry_kind   text not null,                  -- mod | vehicle | save
  target_id    text not null,
  pinned_version text,                         -- null ⇒ 'latest at publish time'
  position     integer not null,
  note         text,
  added_by     bigint references account(id),
  added_at     timestamptz not null default now(),
  primary key (modlist_id, entry_kind, target_id)
);

-- immutable published snapshots
create table modlist_version (
  id            bigserial primary key,
  modlist_id    text not null references modlist(id) on delete cascade,
  version       text not null,                 -- SemVer
  version_sort  bytea not null,
  changelog     text,
  game_min_revision integer,
  game_max_revision integer,
  published_by  bigint not null references account(id),
  published_at  timestamptz not null default now(),
  yanked_at     timestamptz,
  unique (modlist_id, version)
);

create table modlist_pin (
  modlist_version_id bigint not null references modlist_version(id) on delete cascade,
  entry_kind   text not null,
  target_id    text not null,
  version      text not null,                  -- exact pin, never a range
  position     integer not null,
  note         text,
  primary key (modlist_version_id, entry_kind, target_id)
);

-- ─────────── operational ───────────
create table job (
  id            bigserial primary key,
  kind          text not null,                 -- validate_release | reverify | import_repo | export
  payload       jsonb not null,
  state         text not null default 'queued',-- queued|running|done|failed|dead
  attempts      smallint not null default 0,
  run_after     timestamptz not null default now(),
  locked_by     text,
  locked_at     timestamptz,
  last_error    text,
  created_at    timestamptz not null default now()
);
create index on job (state, run_after) where state = 'queued';

create table build (
  revision       integer primary key,
  version_string text not null,
  released_on    date,
  from_revision  integer
);

create table compat_report (
  mod_id     text not null,
  version    text not null,
  revision   integer not null,
  works      boolean not null,
  account_id bigint not null references account(id),
  created_at timestamptz not null default now(),
  unique (mod_id, version, revision, account_id)
);

create table report (
  id         bigserial primary key,
  subject_kind text not null,                  -- mod | release | modlist | account
  subject_id text not null,
  category   text not null,                    -- malware|stolen|broken|other
  reporter   bigint references account(id),
  body       text,
  state      text not null default 'open',
  created_at timestamptz not null default now()
);

create table moderation_action (
  id          bigserial primary key,
  actor       bigint not null references account(id),
  action      text not null,
  subject_kind text not null,
  subject_id  text not null,
  rationale   text not null,
  public      boolean not null default true,
  created_at  timestamptz not null default now()
);

create table resolution_event (                -- NOT a download count (plan §9)
  subject_kind text not null,
  subject_id   text not null,
  kind         text not null,                  -- outbound_click | api_resolve
  occurred_at  timestamptz not null default now()
);
```

### 3.1 Notes on shape

**`citext` for every id lookup column.** RFC 0031's namespace compares case-insensitively while preserving
authored casing. Doing that with a `citext` unique index gets both properties from one column pair and makes it
impossible to forget a `lower()` in a join.

**`version_sort` is a precomputed binary key.** SemVer ordering — including pre-release precedence, where
`1.0.0-alpha.2` sorts below `1.0.0-alpha.10` but above `1.0.0-alpha` — is not expressible in SQL collation.
Compute it once on insert. Doing it in the application on every query makes "list versions, newest first" the
slowest endpoint on the site.

**`game_min_revision` is nullable here**, unlike v0.1. RFC 0017 requires a lower bound for a *usable*
compatibility claim, but the site must be able to hold a release that has not declared one — that is exactly
the Unknown state, and forcing a sentinel value would lose the distinction between "unknown" and "works with
everything".

**`release_asset_id` has no unique constraint, deliberately.** Collisions are the thing being measured; the
index on `asset_id` is what makes "who else declares this?" a single fast lookup, and that query is plan §7's
whole value proposition.

**`modlist_draft_entry.pinned_version` may be null**, meaning "whatever is current when we publish". Resolving
nulls happens at publish time and the resulting `modlist_pin` always carries an exact version. A published
modlist never contains a floating reference.

---

## 4. Identity, auth and permissions

### 4.1 Sign-in

GitHub OAuth and Discord OAuth. No passwords, ever — there is nothing here worth the cost of storing one.

An account may link both providers. GitHub is the more valuable link because it is what makes repository
ownership provable (§5.2); Discord exists because that is where the community is.

Sessions are opaque ids in an `HttpOnly; Secure; SameSite=Lax` cookie, backed by the `session` table so they
can be revoked server-side. 30-day sliding expiry, hard cap at 90 days, and rotation on any privilege change.

### 4.2 Permissions

Two role scopes plus a site scope.

| Scope | Roles |
|---|---|
| Mod | `owner`, `maintainer` |
| Modlist | `owner`, `admin`, `editor` |
| Site | `user`, `moderator`, `admin` |

| Action | Mod owner | Mod maintainer | List owner | List admin | List editor | Moderator |
|---|:--:|:--:|:--:|:--:|:--:|:--:|
| Edit listing metadata | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Connect/disconnect the repository | ✓ | — | — | — | — | — |
| Import or retry a release | ✓ | ✓ | — | — | — | ✓ |
| Yank a release | ✓ | ✓ | — | — | — | ✓ |
| Add/remove maintainers | ✓ | — | — | — | — | ✓ |
| Transfer ownership | ✓ | — | ✓ | — | — | ✓ |
| Edit modlist draft | — | — | ✓ | ✓ | ✓ | — |
| Publish a modlist version | — | — | ✓ | ✓ | — | — |
| Invite/remove collaborators | — | — | ✓ | ✓ | — | ✓ |
| Delete the modlist | — | — | ✓ | — | — | — |
| Delist / take down | — | — | — | — | — | ✓ |

**Editors edit but cannot publish.** Publishing mints an immutable version that other people will install, and
it is a meaningfully different act from adding a mod to a draft. Splitting it is the main reason `admin`
exists as a distinct role from `editor`.

**Exactly one owner per mod and per modlist.** Co-ownership sounds friendly and produces disputes with no
tiebreaker. Transfer is explicit and logged.

### 4.3 Account deletion

A deletion request anonymises the account — handle released, OAuth identities dropped, sessions revoked — and
leaves mods and modlists standing, reassigned to the remaining owner or, failing that, flagged as orphaned.

Deleting a user's published content on request would break every modlist pinning it and every dependency
naming it. Orphaned content becomes claimable through the dispute path (§5.4) after 90 days.

---

## 5. Mods and releases

### 5.1 Creating a mod

A signed-in user creates a mod by supplying an id and the listing metadata. The id is validated against RFC
0031's rules and checked case-insensitively against **both** mods and modlists.

The creator becomes `owner`. There is no separate claim step and no waiting period: a listing with no
connected repository and no releases is inert, and gating it behind review buys nothing.

**Unpublished listings expire.** A mod with no successfully imported release after 90 days is released back to
the namespace, after a warning to the owner. This is plan §3.1's squatting policy; it applies to modlists too.

### 5.2 Connecting a repository, which is also the ownership proof

The owner installs the ksamods app on the repository — a GitHub App, or the equivalent for another supported
forge (§5.6) — and selects it in the UI. The site records the provider, installation and repository ids.

**Installing an app requires admin on that repository.** That is a considerably stronger ownership signal than
a forums thread, and it is the reason this spec does not gate publishing on the forums link that RFC 0031
requires for its own index. The forums link stays recommended, and becomes required at export (§12).

One repository links to one mod. A repository already linked elsewhere is refused with a pointer to the
existing listing — that is a dispute (§5.4), not an error to work around.

### 5.3 Importing a release

On a release-published event — or a release edit, or a manual retry — the forge delivers a webhook. The API
verifies the signature, resolves the installation to a mod, and enqueues an `import_release` job.

The worker then:

1. **Selects the asset.** A single `.zip` asset, or the one matching a configured glob. Zero or several with
   no glob is a failure the author sees on the listing, not a silent skip.
2. **Normalises the version** from the tag to SemVer 2.0.0, stripping a leading `v`. **A tag that does not
   parse fails the import with the error in front of the author**, per RFC 0031, rather than being coerced into
   something plausible.
3. **Refuses to re-stamp.** If that version already exists, the import does not overwrite. Same bytes is a
   no-op; different bytes is a divergence (§11.1).
4. **Derives `release_status`** — `stable`, `testing` or `dev` — from the forge's prerelease flag and the
   SemVer pre-release part, so a nightly does not present itself as a release.
5. **Runs the validation pipeline** (§7).
6. **Snapshots the listing** into `listing_snapshot`, so browsing version 3 shows what version 3 said rather
   than what the listing says today.
7. **Persists** findings, asset ids, dependencies, assemblies and `[console]` — then **discards the archive**.

Steps 3 and 7 are the load-bearing ones. Immutability is what makes a recorded hash worth anything, and
discarding the bytes is what keeps the site an index rather than a host.

### 5.4 Disputes

Two people claiming one id, or a repository already linked, goes to a moderator. Evidence, in descending
weight: app installation on the canonical repository; commit history; the KSA forums thread; the `author`
field in a published `mod.toml`.

**A mod disputing an id held by a modlist is decided by rule, not judgement: the mod wins** (§2.1). The
moderator's job there is to confirm the mod is real and the id is genuinely its folder name, then execute the
rename and the alias.

Outcomes are recorded in `moderation_action` with a rationale and are public by default.

### 5.5 Amending a published release

**[RFC 0031]** A published release accepts a narrow set of amendments and nothing else. Every one records
knowledge gained after publication, and every one may only *narrow* what the release claims:

| Change | Allowed |
|---|:--:|
| Yank, with a reason | ✓ |
| Add or lower `game_max_revision` | ✓ |
| Raise `game_min_revision` | ✓ |
| Add or lower a dependency or loader `max` | ✓ |
| Add or raise a dependency or loader `min` | ✓ |
| Add a missing dependency or `conflict` | ✓ |
| Widen or remove any bound | ✗ |
| Remove a dependency entry | ✗ |
| Change version, artifact URL, hash or install data | ✗ |

The invariant, enforced in the service layer and covered by tests: **a release can never become more
permissive after publication.** A release that turns out to support more than it was stamped with keeps its
stamp, because nobody re-verified the wider claim against the actual archive.

This is why a mod that breaks on newer builds gets its compatibility tightened rather than yanked — it stays
installable where it still works.

**A yank is the author's statement about one build.** Distinct from `status = "deprecated"`, which covers the
whole listing, and from a moderator delisting, which is not the author's voice at all. Three vocabularies;
never merge them.

### 5.6 Supported forges

"GitHub only" would exclude authors who deliberately do not use GitHub, and that is an access decision as much
as a technical one. The properties actually worth keeping are not GitHub's — they are:

- an **enumerable host allowlist**, so §14.1's SSRF surface stays a handful of known domains rather than the
  open internet;
- **app installation as proof of repository control**;
- a **release API** giving tags, prerelease flags, assets and changelogs without scraping;
- **webhooks**, so imports are event-driven rather than polled.

Every mainstream git forge has all four. So the rule is **git forge releases from an allowlist**, not GitHub
specifically:

| Provider | Status |
|---|---|
| GitHub | Supported at launch |
| GitLab (gitlab.com and self-hosted) | Adapter, post-launch |
| Codeberg / Gitea / Forgejo | Adapter, post-launch |

Adding a forge means implementing `IForgeProvider` — resolve installation, list releases, fetch release
metadata, verify webhook signature, enumerate asset URLs — and adding its asset domains to the fetch
allowlist. Nothing else in the pipeline is provider-aware, because §7 receives bytes and an expected id and
does not care where they came from.

**Self-hosted instances are allowlisted individually, not by pattern.** A wildcard over "any GitLab" is an
open redirect into arbitrary hosts wearing a forge's clothes, which gives back exactly the property this
design is buying. An operator adds an instance deliberately, and that is a moderation decision with a record.

**Arbitrary download URLs stay unsupported.** That is where SSRF, link rot and provenance all get materially
worse at once, and a forge allowlist covers the authors who were actually being excluded.

---

## 6. Modlists

### 6.1 Draft and versions

A modlist has one mutable **draft** and zero or more immutable **published versions**.

Collaborators edit the draft freely. Publishing takes a SemVer version and a changelog, resolves every
unpinned draft entry to the target's current version, and writes `modlist_version` plus its `modlist_pin`
rows. The draft survives and continues to be editable.

**Pins are exact, never ranges.** RFC 0031's reasoning transfers exactly: a dependency says "any X in this
range works", a modlist says "this specific set is what I curated and tested". Those are different claims and
only one of them is a range.

**Compatibility is computed, not authored.** A published version's `game_min_revision` is the highest minimum
among its members and its `game_max_revision` the lowest maximum, because a list works only where every member
works. Where that intersection is empty, publishing warns loudly and proceeds — the members may still be
individually installable, and RFC 0017's line is that only Incompatible blocks.

### 6.2 Publish-time validation

Blocking:

- An empty list.
- A pin naming a mod version that does not exist.

Warning, and overridable with an explicit confirmation:

- A member is yanked, delisted or deprecated.
- Two members declare colliding asset ids (§9) — the collision is shown, naming both mods and the ids.
- Members' compatibility ranges do not intersect.
- A member has unsatisfied required dependencies not otherwise in the list.

The dependency check is the one that earns its place. The game has no dependency resolution and StarMap's is
name-only with no user-visible failure, so a modlist missing a dependency produces a mod that silently never
loads. Catching it at publish time is the whole point of curation.

### 6.3 Concurrent editing

Two people editing one draft is the normal case, not the edge case.

`modlist.draft_revision` increments on every draft mutation. Every write carries the revision the client last
read; a mismatch returns `409 Conflict` with the current state so the UI can merge rather than silently
clobber.

Draft operations are deliberately small and commutative where possible — add entry, remove entry, repin,
reorder, edit note — so conflicts are genuinely rare rather than merely rare-looking.

`GET /api/v1/modlists/{id}/activity` returns a recent-changes feed, which is what makes a multi-editor list
comprehensible when you come back to it.

### 6.4 Visibility

`private` (collaborators only), `unlisted` (link-only, excluded from search and export), `public`.

A private modlist is still subject to moderation on report, and the interface should say so rather than
implying privacy from staff.

---

## 7. The validation pipeline

Implements plan §4. Every job runs in its own container.

### 7.1 Fetch outside, validate inside

The single most important structural decision here: **the worker fetches the archive; the container has no
network at all.**

```
worker (host)                          container
─────────────                          ─────────
resolve DNS, SSRF guard (§14.1)
fetch from the forge over TLS
write to a scratch file
                          ─── mount ro ──▶  /in/archive.zip
                          ─── mount rw ──▶  /out/
                                            KsaMods.Validator.Host
                                            → /out/report.json
◀── read report, persist ───
delete scratch file
```

This splits the two risks and handles each where it is cheap. SSRF is a network-policy problem, solved on the
host where DNS and egress can actually be controlled. Malicious archive content is a parsing problem, solved
in a container that cannot reach anything even if the parser is defeated. Doing the fetch inside the container
would force the container to have egress, which is precisely what makes the rest of the isolation worth less.

### 7.2 Container configuration

Non-negotiable, all of it:

```
--network none                 no egress, no DNS, no metadata endpoint
--read-only                    root filesystem immutable
--tmpfs /tmp:size=512m,noexec  scratch, non-executable
--user 65534:65534             nobody; image contains no setuid binaries
--cap-drop ALL                 no capabilities
--security-opt no-new-privileges
--security-opt seccomp=<profile>
--pids-limit 128
--memory 1g --memory-swap 1g   no swap: an OOM kills the job, not the host
--cpus 1.0
--ulimit nofile=256:256
                               timeout 120s, SIGKILL after grace
```

The image is pinned by digest, not by tag, and rebuilt from a locked base on a schedule. **The Docker socket
is never mounted into anything.** The worker talks to the daemon; the container has no idea a daemon exists.

Run the worker on its own host, or at minimum in its own trust zone. It holds the daemon socket, which is
root-equivalent, and it is the process handling attacker-controlled bytes.

### 7.3 Stages

| # | Stage | Fails the release? |
|---|---|---|
| 1 | Fetch: size ceiling, timeout, redirect cap, SSRF guard. Record final URL, status, `ETag`. | yes |
| 2 | Integrity: compute `sha256`. On re-verification, compare and classify (§11.1). | yes |
| 3 | Archive safety: traversal, absolute paths, links, device entries, ratio and size ceilings, nested archives (§14.2). | yes |
| 4 | Structure: exactly one top-level directory, name equal to the mod id case-insensitively, `mod.toml` present and parsing. Derive `install.root`. | yes |
| 5 | Declaration integrity: declared paths resolve; declared XML parses with the correct root; two-hop reachability. | errors yes, warnings no |
| 6 | Asset id extraction. | no |
| 7 | Code facet: entry assembly resolves; reject `KSA.dll`, `Brutal.*`, `Planet.*`; warn on loader assemblies. | rejections yes |
| 7b | Dependency extraction from `[[StarMap.ModDependencies]]`. | no |
| 8 | Risk surface: `[console]` arrays, shipped assemblies, ids colliding with `Core`. | no |

**Stage 5 is the one nobody else does** (plan §0). Reachability is two hops: files named in the six list keys
of `mod.toml`, plus paths referenced from inside those files' XML, transitively. Only files reachable from
neither are warned about. Warning on everything absent from `mod.toml` would fire on the correct shape of
every content mod — textures and meshes belong in the XML, not the manifest — which is how a warning becomes
noise nobody reads.

**Stage 7 never executes anything.** `MetadataLoadContext` reads identity and references without running a
static constructor or module initialiser. §14.4.

**Stage 4's id comparison is case-insensitive but the mismatch is fatal.** The folder name is the identity the
game will see; no amount of correct metadata survives getting it wrong.

### 7.4 Findings

Every finding carries a stable `code` — `KSAM-0501` (declared path missing), `KSAM-0503` (unreachable content
file), `KSAM-0701` (game assembly shipped). Codes are the contract: messages get reworded, codes get filtered
on, by the UI and by anyone consuming the export.

The pipeline is **total, not fail-fast**: a stage records findings and continues wherever continuing is
meaningful, so an author gets every problem in one run instead of one per push. Only genuinely
uninterpretable input — a corrupt archive, an unparseable `mod.toml` — short-circuits.

### 7.5 Outcomes

`passed`, `passed_warnings` or `failed`. Warnings never block publication; they appear on the listing.

A failed import leaves the release visible to its maintainers with the full report and a retry button, and
invisible to everyone else. Authors fixing a packaging mistake should not have to delete and recreate
anything.

---

## 8. Compatibility

Follows RFC 0017; spec §16 carries the evidence.

**Order by the revision alone** — the fourth component of `Year.Month.Build.Revision`. Not the dotted string,
which misorders 21 adjacent pairs across the shipped history because the third component is a machine-local
counter that *decreases* 32 times as the revision rises.

**Authors write a version, the site stores a revision.** `2026.8.3.5117`, or a month like `2026.7` meaning
that whole calendar month, resolved against the `build` table at save time and stored as an integer. Storing
the display string alone would make every compatibility check a join and would break the moment the export
needs to be evaluable offline.

**Four states; only one blocks:**

| Condition | State | Client behaviour |
|---|---|---|
| no usable minimum | Unknown | listed, installable after confirmation |
| `r < min` | Incompatible | **blocked**, showing what it needs |
| `max` absent, or `r <= max` | Compatible | installs normally |
| `r > max` | Untested | installs after confirmation |

The game validates nothing, so a false "incompatible" takes a working mod away for no reason while a false
"compatible" is a mod that does not load and can be removed. Warn, do not block.

**Do not build a build list.** `builds.json` in KSAModding/KSA-CKAN-meta is updated hourly by an Action
polling the game's own master server and has kept itself current since 2026-07-02 with no human attention.
Mirror it (§11.2).

---

## 9. Conflict detection

KSA registers asset ids into one global table with `TryAdd`, so a duplicate id from a later mod is silently
discarded — no error, no log line a user will ever find. Load order comes from `manifest.toml` order.

Because §7 stage 6 already extracted every asset id, the site can do what no generic mod host can:

- **`GET /api/v1/collisions/{assetId}`** — every release declaring it. One indexed lookup.
- **On a mod page** — which other mods declare ids this mod also declares.
- **At modlist publish** — collisions within the curated set (§6.2).
- **In a resolve response** — collisions within the resolved set, so a client can warn before writing.

**Core override detection.** An id also present in `Core` means the mod overrides stock content, which
requires load-order placement before `Core` and is fragile because the game rewrites `manifest.toml` freely.
Flag it, show which ids, and mark the mod as a distinct and fragile category rather than letting a user find
out by having their game change.

---

## 10. API

### 10.1 Read

```
GET  /api/v1/mods                          search, filter, facet
GET  /api/v1/mods/{id}                     listing + releases
GET  /api/v1/mods/{id}/releases/{version}  one release: artifact, hash, deps, findings
GET  /api/v1/modlists                      public lists
GET  /api/v1/modlists/{id}                 metadata + versions
GET  /api/v1/modlists/{id}/versions/{v}    pinned set
GET  /api/v1/builds                        known game builds
GET  /api/v1/collisions/{assetId}          who else declares this id
GET  /api/v1/index.json                    full export snapshot (§12)
POST /api/v1/resolve                       { content[], gameBuild } → ordered install plan
```

### 10.2 Write

```
POST   /api/v1/mods                              create a listing
PATCH  /api/v1/mods/{id}                         edit metadata
POST   /api/v1/mods/{id}/repo-link               connect a repository
POST   /api/v1/mods/{id}/releases/import         manual import or retry
POST   /api/v1/mods/{id}/releases/{v}/yank       yank, with reason
PATCH  /api/v1/mods/{id}/releases/{v}            narrowing amendments only (§5.5)
POST   /api/v1/mods/{id}/maintainers             add
DELETE /api/v1/mods/{id}/maintainers/{account}   remove

POST   /api/v1/modlists                          create
PATCH  /api/v1/modlists/{id}                     edit metadata
PUT    /api/v1/modlists/{id}/draft               replace draft (If-Match: draft_revision)
POST   /api/v1/modlists/{id}/draft/entries       add entry
DELETE /api/v1/modlists/{id}/draft/entries/{k}/{target}
POST   /api/v1/modlists/{id}/publish             snapshot draft → version
POST   /api/v1/modlists/{id}/collaborators       invite
GET    /api/v1/modlists/{id}/activity            recent changes

POST   /api/v1/reports                           report anything
```

### 10.3 Rules

- **Unauthenticated reads** for everything public. Session cookie for writes; a Bearer PAT for scripted use.
- **Every artifact carries its `sha256`, always.** A client that does not verify is non-conforming (plan §5),
  and the API's job is to make verifying the easy path.
- **ETags on read**, derived from the subject's `updated_at` and the latest relevant job completion.
- **Keyset pagination**, not offset. Offset pagination over a table changing mid-scroll skips and repeats rows,
  and the offline-snapshot use case makes stable iteration a requirement.
- **`spec_version` on every document**, so a client meeting a version it does not implement renders the entry
  as unknown rather than dropping it.
- **Idempotency-Key** honoured on `POST /publish` and `/import`, both of which are expensive and both of which
  users will double-click.
- **`409 Conflict` carries the current state**, not just an error. That is what lets a UI merge.

### 10.4 `POST /resolve`

Takes a desired set and a game build revision; returns an ordered install plan, or a structured explanation of
why the set is unsatisfiable, plus the asset id collisions within the resolved set.

`KsaMods.Resolver` **ships as a library as well as running behind the endpoint.** The offline index export
means clients must be able to solve locally, so a server-only solver merely guarantees a second, subtly
different implementation appears — which is how ecosystems get divergent resolution behaviour. One library,
two call sites.

The returned order is what a client writes into `manifest.toml`. It is not a promise about initialisation
order: StarMap defers mods with unmet dependencies through its waiting graph and reorders initialisation
within the manifest walk (spec §13).

---

## 11. Background jobs

A single `job` table consumed with `FOR UPDATE SKIP LOCKED`. Exponential backoff, five attempts, then `dead`
with an alert. Every handler is idempotent, because at-least-once delivery is the only guarantee worth
building on.

### 11.1 Re-verification

Weekly for `stable`, monthly for `testing` and `dev`, sharded so a run is bounded.

| Outcome | Action |
|---|---|
| Hash matches | `availability = verified`, update `last_verified_at` |
| Asset gone (404/410) | `availability = unavailable`, notify maintainers, **keep the record** — clients may have it cached and modlists still pin it |
| Bytes changed, benign re-pack | `availability = diverged`, flag on the page, notify, ask for a real version. **The stamp does not change.** |
| Bytes changed, material change | `availability = quarantined`, hide the download, notify maintainers and moderators, require a human |

"Benign" means re-running stages 3–8 and diffing the extracted facts: same asset ids, same shipped assemblies,
same `[console]` arrays, no new file types. A changed assembly list, a new `[console]` entry or new asset ids
is material.

**Keeping the false-positive rate low is what keeps this working.** Authors re-upload assets routinely — a bad
build, a CI re-run — and a queue that is mostly noise trains moderators to click accept, which is exactly the
wrong reflex on the day a real compromise arrives.

A forge gives a corroborating signal an arbitrary host would not: the release's `updated_at` and whether the
asset was replaced. Use it to distinguish an author re-uploading from bytes changing underneath a release
nobody touched. The second is far more alarming than the first, and only a forge lets you tell them apart.

### 11.2 Others

| Job | Cadence | Notes |
|---|---|---|
| `sync_builds` | hourly | Mirror `builds.json` into `build`. |
| `export_index` | on change, at most every 15 min | Build the export **and push the git mirror** (§12.1). Push failure alerts. |
| `expire_listings` | daily | 90-day unpublished listings and modlists. |
| `sweep_installations` | daily | Reconcile forge app installs; a revoked install disables auto-import and notifies. |
| `link_health` | weekly | Check `links.*`; a dead forums thread is a moderation signal. |
| `refresh_search` | on change | Materialised search vectors. |

---

## 12. Export and interop

Postgres holding the record means the metadata does not outlive the site by default. The export is what buys
that back, and it is also what makes the data useful to Borea and to CKAN-KSA.

`KsaMods.Exporter` produces **RFC 0031-shaped documents**: one authored TOML per listing, one generated JSON
per release, one TOML per published modlist version. Bundled as `index.tar.gz` plus a served `index.json`,
with a manifest carrying `spec_version`, generation time and a content hash.

### 12.1 The git mirror is required

**Every export run commits and pushes to a public git repository.** Not optional, not a later phase — it is
the mitigation for the single largest cost of moving off git-as-record (§0.2), and it is a few hours of work.

A tarball behind a CDN dies with the CDN and with whoever pays for it. A public git repository survives
because it gets forked, and — this is the part that matters — **forks happen before the outage, not after**.
Nobody clones a backup they did not know existed.

Properties to hold:

- **Deterministic output.** Stable key ordering, stable file ordering, timestamps only where semantically
  meaningful. A run that changes nothing must produce no commit; otherwise the history is noise and diffing
  two days apart tells you nothing.
- **One commit per export run**, with a message naming what changed and how many records.
- **The moderation log is exported too.** §13.4 — an audit trail that only exists inside the service is only
  as trustworthy as the service.
- **Push failure is an alert, not a warning.** A silently stale mirror is worse than no mirror, because it
  looks like a backup.

What the mirror does *not* do: it is a follower, so it inherits whatever the writer produced. A buggy or
compromised writer publishes bad state into it, where a PR-based flow would have caught that at review. It
guarantees the data outlives the service. It does not guarantee the data is right.

### 12.2 Conformance and ingest

**Export requires the fields RFC 0031 requires**, including `links.forums`, which the site does not otherwise
gate on (§5.2). A listing missing them is skipped, and its maintainers are told why, on the listing. Exporting
a document that does not satisfy the format it claims to implement would make the export worse than useless.

Modlist aliases (§2.1) are exported as a redirect map so a consumer holding a retired id can follow it.

Ingesting the KSAModding index — plan §0's peer posture — reads their published index and surfaces listings
absent here, labelled by source. It is deliberately read-only and deliberately does not merge: two indexes
accepting claims independently makes divergent ownership of an id the expected steady state, and a merge
policy manages that at best. **The real fix is agreeing a posture with the KSAModding maintainers** (§19).

---

## 13. Moderation

### 13.1 Vocabularies

- **Author's voice:** `status = deprecated` and `superseded_by` on a listing; `yanked` on one release.
- **Site's voice:** `unlisted`, `delisted`, `taken_down`. Never author-writable.

Keep them separate in the schema and in the UI. Collapsing them is very hard to undo once published data
exists, and it makes every "is this gone, and who says so?" question ambiguous.

### 13.2 Review queue

Automatic entry for: a first release from a new account, any release shipping a DLL, any `[console]` usage,
any quarantine, and any report.

Review gates *listing*, never *import*. A release under review is visible to its maintainers with its full
validation report, so the author's iteration loop is never blocked on a human.

### 13.3 Takedown

**A takedown is a delisting, not a deletion.** The site does not host, so removing a listing does not remove
anything from the author's forge. Say exactly that rather than implying a power the site does not have.

**Delisted content stays resolvable by id.** Modlists pin it and dependency graphs reference it; a hole in the
graph is a worse failure than a listing marked delisted. Serve the record, refuse to offer the download, state
why.

### 13.4 Audit

Every moderation action writes `moderation_action` with a rationale, public by default (plan §12). Under
git-as-record this was `git log`; now it is a table, and a table is only as trustworthy as the code that
writes it. Three properties close that gap:

- **Append-only.** No `UPDATE`, no `DELETE`, enforced by a database role that lacks both rather than by
  convention. A correction is a new row referencing the old one.
- **Exported to the git mirror** (§12.1), so the audit trail exists somewhere the service cannot rewrite.
- **Written in the same transaction as the effect.** A delisting that lands without its log row, or a log row
  without its delisting, is worse than either alone.

### 13.5 Stolen content

The community norm is that decompiled game code is not published. A mod shipping decompiled `KSA.dll` sources
or redistributing game assets wholesale is a reportable category with a documented rule, not a judgement call
made per incident.

---

## 14. Security

Ranked worst-first.

### 14.1 SSRF at fetch

The worker fetches URLs derived from webhook payloads. Restricting sources to allowlisted forges narrows this
considerably but does not close it: release assets redirect, and a compromised or hostile app installation
supplies attacker-chosen values.

- **Allowlist hosts to the registered asset domains of supported forges** (§5.6), including any individually
  approved self-hosted instances. This is the single biggest win of the forge-only decision and it must be
  enforced at fetch time, not assumed from the fact that a webhook arrived.
- Resolve DNS explicitly and reject loopback, link-local, private, CGNAT, multicast and reserved ranges, IPv6
  and IPv4-mapped forms included.
- **Re-check after every redirect.** Validating only the initial URL is the standard way this is got wrong.
- Pin the resolved address for the connection so DNS cannot change between check and connect.
- Cap redirects at 5, total time at 120s, body at the archive ceiling.
- `https` only.

### 14.2 Untrusted archives

Never `ExtractToDirectory`. Iterate entries; for each:

- Reject absolute paths, `..` traversal, anything whose normalised path escapes the root.
- Reject symlinks, hardlinks, device entries, any non-regular file.
- Reject nested archives.
- Enforce entry count, per-entry uncompressed size, total uncompressed size **and** compression ratio. All
  four — a zip bomb defeats any three alone.
- Strip `__MACOSX/`, `.DS_Store`, `Thumbs.db`, `desktop.ini`.
- Reject `manifest.toml` and `settings.toml` anywhere in the tree (spec §8).
- Treat entry names as untrusted for the filesystem, for logs, and for the database.

Starting ceilings: 50 MB compressed, 500 MB uncompressed, 10,000 entries, ratio 100:1.

### 14.3 XML

`XmlReaderSettings` with `DtdProcessing = Prohibit`, `XmlResolver = null`, a `MaxCharactersFromEntities` cap
and a depth limit. Closes XXE, billion-laughs, and external entity fetches — the last of which would otherwise
be a second SSRF surface reachable from inside a mod archive. The container having no network makes that one
unexploitable rather than merely mitigated, which is defence in depth working as intended.

### 14.4 Never execute mod code

`MetadataLoadContext` only. **`Assembly.Load` and `Assembly.LoadFrom` are banned**, enforced by a Roslyn
analyser rather than review, because one careless call turns metadata inspection into arbitrary code execution.

### 14.5 The container boundary

The worker holds the Docker socket, which is root-equivalent on its host. Treat it accordingly: its own host
or trust zone, no other services, no inbound network, credentials scoped to the queue and the object store and
nothing else.

### 14.6 Webhooks

Verify the HMAC signature on every delivery with a constant-time comparison before parsing the body. Reject
deliveries older than five minutes. De-duplicate on delivery id — forges retry, and an import that runs twice
must be a no-op.

### 14.7 The threat the site cannot fix

**Code mods are unsandboxed .NET assemblies with full process privileges.** Nothing in validation or review
changes that. Say so plainly on every page offering a code mod rather than letting a green badge imply a
safety review that did not happen.

### 14.8 Ordinary hygiene

Rate limit writes per account and reads per IP. CSP without `unsafe-inline`. Markdown rendered with a
strict allowlist and sanitised — `description` is user input rendered to other users, which makes it the most
likely XSS vector in the product. Secrets in a real secret store. Dependency scanning in CI.

---

## 15. Performance

The read path dominates and is highly cacheable.

- CDN in front of everything public; invalidate on write, not on a timer.
- `Cache-Control: public, max-age=60, stale-while-revalidate=86400` on listing reads. Slightly stale metadata
  is fine; being down is not.
- `index.json` is a generated artifact served from object storage, never a query. It is the largest response
  and the easiest to get wrong by building it per request.
- Search via Postgres `tsvector` with a materialised column. Enough for a catalogue in the low thousands, and
  it removes a whole service from the deployment. Revisit at ~10k listings, not before.
- Precompute per-mod aggregates — latest stable version, compatibility span, collision count — in a
  materialised view refreshed on write. Listing pages should be one query.

---

## 16. Operations

### 16.1 Deployment

Three processes: API, worker, exporter. Managed Postgres. Object storage for exports. A CDN.

The worker is the one that needs its own host, for §14.5.

Realistic cost at low volume: a small API host, a small worker host, managed Postgres, object storage and a
CDN — roughly $40–70/month, with bandwidth the variable. Validation is CPU-bursty and idle most of the time,
which suits a small host with a queue rather than anything autoscaled.

### 16.2 Backups

Postgres is now the record, so this is load-bearing in a way it was not in v0.1: daily snapshots, PITR,
30-day retention, and a **restore that has actually been performed**. An untested restore is a hope, and the
data it protects is the entire product.

**The git mirror (§12.1) is a second, independent copy held by people who are not you**, which is the property
no snapshot policy provides. It carries the published metadata, not accounts or sessions — so a total loss
still costs the user table, but the catalogue survives and can be rebuilt into a fresh instance or a fork.

Alert on mirror push failure at the same severity as a backup failure, because that is what it is.

### 16.3 Observability

Structured logs correlated across request → job → container → result. Metrics that matter: queue depth and
age, validation pass/fail by stage, container OOM and timeout counts, re-verification outcomes, quarantine
depth, webhook delivery failures, p99 read latency.

Alert on queue age, dead jobs, quarantine depth and webhook failure rate. All four mean something users will
notice before you do.

---

## 17. Testing

**A fixture corpus of real archives** — code mod, content mod, hybrid — plus one deliberately broken archive
per stage: unresolved declared path, malformed XML, wrong-named root, zip bomb, traversal entry, shipped
`KSA.dll`, `[console]` present. Every stage gets a fixture that fails it. This is the highest-value test asset
in the project and it should exist before the pipeline does.

**Property tests** for the id rules and the version comparator — small total functions over large input spaces
with subtle boundaries (trailing dots, dotted device names, revision ties). The version comparator has a free
oracle: RFC 0017 states that sorting the 155 shipped releases by revision reproduces true release order while
four-part sorting misorders 21 adjacent pairs. **Encode that as a test.**

**Container escape tests** in CI: an archive that tries to write outside `/out`, one that tries to open a
socket, one that allocates unboundedly. Each must fail closed. Isolation that is never tested is isolation
that quietly regressed three deploys ago.

**Concurrency tests** for §6.3 — two simultaneous draft writes must produce one success and one `409`, never
a lost update.

**Amendment invariant tests** for §5.5, enumerating every field and asserting that widening is rejected.

---

## 18. Build order

**Phase 1 — the core loop.** Auth, mod creation, GitHub linking, release import, the validator library and
container, findings on the listing page, **and the export with its git mirror**. This is the shortest path to
something a mod author gets value from, and it forces the metadata types to be right before anything depends
on them.

The mirror belongs here rather than in a later phase for two reasons: it is the durability story for a
database that is now the record, and it forces the RFC 0031 serialisation to be correct from the first
listing rather than retrofitted onto data that grew without it.

**Phase 2 — modlists.** Drafts, collaborators, publishing, versioned pins, publish-time checks, aliases.

**Phase 3 — the read product.** Search, facets, compatibility surfacing, public read API, `builds.json` sync.

**Phase 4 — trust.** Re-verification, divergence classification, quarantine, review queue, reports,
moderation log.

**Phase 5 — the ecosystem.** Resolver library and endpoint, collision surfacing, CLI, additional forge
adapters, upstream index ingest.

Stages 6 and 7b of the pipeline run from the first import even though nothing consumes them until Phase 5.
Both are nearly free at ingest and impossible to backfill once asset URLs start rotting — the site does not
keep the bytes, so a fact not extracted on first contact may be unrecoverable.

---

## 19. Open questions

### Decided since v0.2

| Was open | Decision |
|---|---|
| GitHub-only excludes non-GitHub authors | **Generalised to a forge allowlist** (§5.6). GitHub at launch; GitLab and Codeberg/Forgejo by adapter. The properties worth keeping were an enumerable host allowlist, app-install proof, a release API and webhooks — every mainstream forge has all four, so none of them required GitHub specifically. Arbitrary URLs stay unsupported. |
| Should modlists share the mod id namespace? | **Yes, shared — with mods holding priority** (§2.1). A mod's id is forced by the game; a modlist's is a free choice, and renaming a modlist is survivable via an alias while renaming a mod breaks every install. The constrained party wins. |
| Is the git mirror worth it? | **Required, in Phase 1** (§12.1). It is the mitigation for the largest cost of leaving git-as-record, and it only works if forks exist before the outage. |

### Still open

1. **Posture with KSAModding (plan §0).** §12's read-only ingest manages a problem that should be designed
   away. Still the first question, and it gets more expensive with every listing.
2. **Does Borea consume the validation findings?** Asset collisions, unreachable content files and unresolved
   declared paths are this project's distinctive contribution, and they are only worth surfacing if a client
   uses them. Worth confirming early — it justifies a good deal of Phase 1.
3. **Tomlyn or Tomlet?** StarMap parses `mod.toml` with Tomlet. If they disagree on a malformed file, the
   validator's verdict and the loader's behaviour diverge, which is the one place this must not be wrong. A
   differential test over the fixture corpus would settle it cheaply.
4. **Who operates and pays, and what happens when they stop?** §12.1 and §16.2 make the metadata survive. They
   do not make the site survive, and accounts and modlist drafts are not in the mirror.
5. **Do vehicles and saves land here?** RFC 0025 puts them in scope as separate content types with a different
   install target. §3's schema assumes they fit the existing shape, and `modlist_draft_entry.entry_kind`
   anticipates them. Probably right. Not verified.
6. **Multi-owner mods.** §4.2 allows exactly one owner plus maintainers. Some mods are genuinely
   collaborative, and the modlist model already supports shared control — worth revisiting once there is
   evidence of the need rather than in anticipation of it.
7. **Which self-hosted forge instances get allowlisted, and on what basis?** §5.6 makes it a deliberate
   moderation decision with a record. It needs a stated bar before the first request arrives, not after.
