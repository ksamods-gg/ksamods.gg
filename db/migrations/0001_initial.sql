-- ksamods.gg initial schema (backend.md §3).
--
-- Postgres is the store of record. The published export and its git mirror (§12.1) are what let
-- the metadata outlive the service; nothing in here is derived from a git tree.

begin;

create extension if not exists citext;

-- ═══════════════════════════ identity (§4) ═══════════════════════════

create table account (
  id             bigserial primary key,
  handle         citext      not null unique,
  display_name   text        not null,
  github_user_id bigint      unique,
  github_login   text,
  discord_id     text        unique,
  forums_url     text,                              -- recommended; required to export (§12.2)
  avatar_url     text,
  site_role      text        not null default 'user',
  created_at     timestamptz not null default now(),
  suspended_at   timestamptz,

  constraint account_site_role_valid check (site_role in ('user', 'moderator', 'admin'))
);

create table oauth_identity (
  account_id bigint      not null references account(id) on delete cascade,
  provider   text        not null,
  subject    text        not null,
  linked_at  timestamptz not null default now(),

  primary key (provider, subject),
  constraint oauth_provider_valid check (provider in ('github', 'discord'))
);
create index oauth_identity_account_idx on oauth_identity(account_id);

-- Sessions are rows rather than self-contained tokens so they can be revoked server-side.
create table session (
  id         uuid        primary key,
  account_id bigint      not null references account(id) on delete cascade,
  issued_at  timestamptz not null default now(),
  expires_at timestamptz not null,
  revoked_at timestamptz,
  user_agent text,
  ip_hash    bytea                                  -- hashed, never stored raw
);
create index session_account_idx on session(account_id) where revoked_at is null;
create index session_expiry_idx on session(expires_at) where revoked_at is null;

-- ═══════════════════════════ mods (§5) ═══════════════════════════

create table mod (
  id            text        primary key,            -- canonical casing, the KSA folder name
  id_lower      citext      not null unique,        -- every join and lookup uses this
  type          text        not null default 'mod',
  name          text        not null,
  abstract      text        not null,
  description   text,                               -- CommonMark; sanitised on render (§14.8)
  license       text        not null,               -- SPDX expression
  tags          text[]      not null default '{}',
  links         jsonb       not null default '{}'::jsonb,
  status        text        not null default 'active',
  superseded_by text        references mod(id),
  listing_state text        not null default 'listed',
  os            text[],
  created_by    bigint      not null references account(id),
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now(),

  constraint mod_id_lower_matches check (id_lower = lower(id)),
  constraint mod_type_valid check (type in ('mod', 'mod-loader')),
  -- The author's voice only. Index-side state lives in listing_state (§13.1).
  constraint mod_status_valid check (status in ('active', 'deprecated')),
  constraint mod_listing_state_valid
    check (listing_state in ('listed', 'unlisted', 'delisted', 'taken_down')),
  -- superseded_by is only meaningful alongside deprecation: content can be abandoned without
  -- a successor, but not succeeded without being deprecated.
  constraint mod_superseded_requires_deprecated
    check (superseded_by is null or status = 'deprecated')
);
create index mod_tags_idx on mod using gin(tags);
create index mod_listed_idx on mod(updated_at desc) where listing_state = 'listed';

create table mod_maintainer (
  mod_id     text        not null references mod(id) on delete cascade,
  account_id bigint      not null references account(id) on delete cascade,
  role       text        not null,
  added_by   bigint      references account(id),
  added_at   timestamptz not null default now(),

  primary key (mod_id, account_id),
  constraint mod_maintainer_role_valid check (role in ('owner', 'maintainer'))
);
create index mod_maintainer_account_idx on mod_maintainer(account_id);

-- Exactly one owner per mod: co-ownership sounds friendly and produces disputes with no
-- tiebreaker (§4.2).
create unique index mod_single_owner_idx on mod_maintainer(mod_id) where role = 'owner';

-- The connected repository. Installing the app requires admin on it, which is the ownership
-- proof this site uses in place of RFC 0031's forums thread (§5.2).
create table repo_link (
  mod_id          text        primary key references mod(id) on delete cascade,
  provider        text        not null,
  installation_id text,
  repo_id         text        not null,
  repo_full_name  text        not null,
  linked_by       bigint      not null references account(id),
  linked_at       timestamptz not null default now(),
  auto_import     boolean     not null default true,
  asset_glob      text,
  last_seen_at    timestamptz,

  -- One repository links to one mod. A repository already linked elsewhere is a dispute, not
  -- an error to work around (§5.2).
  constraint repo_link_unique_repo unique (provider, repo_id),
  constraint repo_link_provider_valid check (provider in ('github', 'gitlab', 'codeberg'))
);

create table mod_release (
  id                  bigserial   primary key,
  mod_id              text        not null references mod(id) on delete cascade,
  version             text        not null,         -- SemVer 2.0.0, normalised
  version_sort        bytea       not null,         -- precomputed; see SemVer.ToSortKey (§3.1)
  release_status      text        not null,
  released_at         timestamptz not null,
  provider            text        not null,
  provider_release_id text,
  provider_tag        text,
  provider_commit     text,
  changelog_url       text,
  changelog_body      text,
  game_min_revision   integer,                      -- null ⇒ Unknown (RFC 0017), not "any"
  game_max_revision   integer,
  game_min_display    text,
  game_max_display    text,
  loader_id           text        references mod(id),
  loader_min          text,
  loader_max          text,
  install_root        text,
  install_derived     boolean,
  install_size        bigint,
  listing_snapshot    jsonb       not null,
  validation_state    text        not null default 'pending',
  availability        text        not null default 'unverified',
  last_verified_at    timestamptz,
  yanked_at           timestamptz,
  yanked_reason       text,
  created_at          timestamptz not null default now(),

  constraint mod_release_unique_version unique (mod_id, version),
  constraint mod_release_status_valid check (release_status in ('stable', 'testing', 'dev')),
  constraint mod_release_validation_valid
    check (validation_state in ('pending', 'running', 'passed', 'passed_warnings', 'failed')),
  constraint mod_release_availability_valid
    check (availability in ('unverified', 'verified', 'unavailable', 'diverged', 'quarantined')),
  constraint mod_release_range_ordered
    check (game_max_revision is null or game_min_revision is null
           or game_max_revision >= game_min_revision)
);
create index mod_release_order_idx on mod_release(mod_id, version_sort desc);
create index mod_release_reverify_idx on mod_release(last_verified_at)
  where availability in ('verified', 'unverified');

create table release_artifact (
  release_id   bigint not null references mod_release(id) on delete cascade,
  url          text   not null,
  is_mirror    boolean not null default false,
  asset_id     text,
  sha256       bytea  not null,
  size         bigint not null,
  content_type text   not null default 'application/zip',

  primary key (release_id, url),
  constraint release_artifact_sha256_length check (octet_length(sha256) = 32)
);

-- ═══════════════════ what validation learned (§7) ═══════════════════

-- Deliberately no unique constraint: collisions are the thing being measured.
create table release_asset_id (
  release_id bigint not null references mod_release(id) on delete cascade,
  asset_id   text   not null,
  xml_path   text   not null
);
create index release_asset_id_lookup_idx on release_asset_id(asset_id);
create index release_asset_id_release_idx on release_asset_id(release_id);

create table release_dependency (
  release_id  bigint not null references mod_release(id) on delete cascade,
  dep_id      text,
  group_id    integer,                              -- non-null groups any_of alternatives
  kind        text   not null,
  min_version text,
  max_version text,
  source      text   not null,

  constraint release_dependency_kind_valid
    check (kind in ('required', 'optional', 'recommends', 'suggests', 'conflict')),
  constraint release_dependency_source_valid check (source in ('authored', 'derived')),
  constraint release_dependency_has_target check (dep_id is not null or group_id is not null)
);
create index release_dependency_release_idx on release_dependency(release_id);
create index release_dependency_target_idx on release_dependency(dep_id);

create table release_assembly (
  release_id       bigint  not null references mod_release(id) on delete cascade,
  file_path        text    not null,
  assembly_name    text,
  assembly_version text,
  is_entry         boolean not null default false,

  primary key (release_id, file_path)
);

create table release_finding (
  release_id bigint   not null references mod_release(id) on delete cascade,
  stage      smallint not null,
  severity   text     not null,
  code       text     not null,                     -- the contract; see FindingCodes (§7.4)
  message    text     not null,
  path       text,

  constraint release_finding_severity_valid check (severity in ('info', 'warning', 'error'))
);
create index release_finding_release_idx on release_finding(release_id);
create index release_finding_code_idx on release_finding(code);

create table release_console (
  release_id bigint not null references mod_release(id) on delete cascade,
  hook       text   not null,
  ordinal    int    not null,
  command    text   not null,

  primary key (release_id, hook, ordinal),
  constraint release_console_hook_valid check (hook in ('onBoot', 'onLoad'))
);

-- ═══════════════════════════ modlists (§6) ═══════════════════════════

create table modlist (
  id             text        primary key,
  id_lower       citext      not null unique,
  name           text        not null,
  abstract       text        not null,
  description    text,
  license        text        not null default 'CC0-1.0',
  tags           text[]      not null default '{}',
  links          jsonb       not null default '{}'::jsonb,
  visibility     text        not null default 'private',
  listing_state  text        not null default 'listed',
  draft_revision integer     not null default 0,    -- optimistic concurrency (§6.3)
  created_by     bigint      not null references account(id),
  created_at     timestamptz not null default now(),
  updated_at     timestamptz not null default now(),

  constraint modlist_id_lower_matches check (id_lower = lower(id)),
  constraint modlist_visibility_valid check (visibility in ('private', 'unlisted', 'public')),
  constraint modlist_listing_state_valid
    check (listing_state in ('listed', 'unlisted', 'delisted', 'taken_down'))
);
create index modlist_public_idx on modlist(updated_at desc)
  where visibility = 'public' and listing_state = 'listed';

-- Retired modlist ids, kept forever. A mod disputing an id held by a modlist wins by rule
-- (§2.1), and this is what keeps the old links and exports resolving afterwards.
create table modlist_alias (
  alias_lower citext      primary key,
  modlist_id  text        not null references modlist(id) on delete cascade,
  retired_at  timestamptz not null default now(),
  reason      text
);

create table modlist_collaborator (
  modlist_id  text        not null references modlist(id) on delete cascade,
  account_id  bigint      not null references account(id) on delete cascade,
  role        text        not null,
  invited_by  bigint      references account(id),
  accepted_at timestamptz,

  primary key (modlist_id, account_id),
  constraint modlist_collaborator_role_valid check (role in ('owner', 'admin', 'editor'))
);
create index modlist_collaborator_account_idx on modlist_collaborator(account_id);
create unique index modlist_single_owner_idx on modlist_collaborator(modlist_id) where role = 'owner';

create table modlist_invite (
  id              uuid        primary key,
  modlist_id      text        not null references modlist(id) on delete cascade,
  email_or_handle text        not null,
  role            text        not null,
  created_by      bigint      not null references account(id),
  created_at      timestamptz not null default now(),
  expires_at      timestamptz not null,
  accepted_by     bigint      references account(id),

  constraint modlist_invite_role_valid check (role in ('admin', 'editor'))
);

-- The mutable working set. pinned_version may be null, meaning "whatever is current when we
-- publish"; publishing resolves it so a published modlist never contains a floating reference.
create table modlist_draft_entry (
  modlist_id     text        not null references modlist(id) on delete cascade,
  entry_kind     text        not null,
  target_id      text        not null,
  pinned_version text,
  position       integer     not null,
  note           text,
  added_by       bigint      references account(id),
  added_at       timestamptz not null default now(),

  primary key (modlist_id, entry_kind, target_id),
  constraint modlist_draft_entry_kind_valid check (entry_kind in ('mod', 'vehicle', 'save'))
);

create table modlist_version (
  id                bigserial   primary key,
  modlist_id        text        not null references modlist(id) on delete cascade,
  version           text        not null,
  version_sort      bytea       not null,
  changelog         text,
  -- Computed at publish, not authored: a list works only where every member works (§6.1).
  game_min_revision integer,
  game_max_revision integer,
  published_by      bigint      not null references account(id),
  published_at      timestamptz not null default now(),
  yanked_at         timestamptz,
  yanked_reason     text,

  constraint modlist_version_unique unique (modlist_id, version)
);
create index modlist_version_order_idx on modlist_version(modlist_id, version_sort desc);

-- Exact pins, never ranges: a dependency says "any X in this range works", a modlist says
-- "this specific set is what I curated and tested" (§6.1).
create table modlist_pin (
  modlist_version_id bigint  not null references modlist_version(id) on delete cascade,
  entry_kind         text    not null,
  target_id          text    not null,
  version            text    not null,
  position           integer not null,
  note               text,

  primary key (modlist_version_id, entry_kind, target_id),
  constraint modlist_pin_kind_valid check (entry_kind in ('mod', 'vehicle', 'save'))
);

-- ═══════════════════════════ operational (§11) ═══════════════════════════

create table job (
  id         bigserial   primary key,
  kind       text        not null,
  payload    jsonb       not null,
  state      text        not null default 'queued',
  attempts   smallint    not null default 0,
  run_after  timestamptz not null default now(),
  locked_by  text,
  locked_at  timestamptz,
  last_error text,
  created_at timestamptz not null default now(),

  constraint job_state_valid check (state in ('queued', 'running', 'done', 'failed', 'dead'))
);
-- Partial index supporting the FOR UPDATE SKIP LOCKED claim query.
create index job_claim_idx on job(run_after, id) where state = 'queued';

create table build (
  revision       integer primary key,
  version_string text    not null,                  -- as the game displays it, counter included
  released_on    date,
  from_revision  integer
);

create table compat_report (
  mod_id     text        not null references mod(id) on delete cascade,
  version    text        not null,
  revision   integer     not null,
  works      boolean     not null,
  account_id bigint      not null references account(id) on delete cascade,
  created_at timestamptz not null default now(),

  constraint compat_report_one_per_user unique (mod_id, version, revision, account_id)
);

create table report (
  id           bigserial   primary key,
  subject_kind text        not null,
  subject_id   text        not null,
  category     text        not null,
  reporter     bigint      references account(id) on delete set null,
  body         text,
  state        text        not null default 'open',
  created_at   timestamptz not null default now(),

  constraint report_subject_kind_valid
    check (subject_kind in ('mod', 'release', 'modlist', 'account')),
  constraint report_category_valid
    check (category in ('malware', 'stolen', 'broken', 'other')),
  constraint report_state_valid check (state in ('open', 'triaged', 'actioned', 'dismissed'))
);
create index report_open_idx on report(created_at) where state = 'open';

-- Append-only. Under git-as-record this was `git log`; now it is a table, and a table is only as
-- trustworthy as the code that writes it (§13.4). A correction is a new row referencing the old,
-- never an edit. The trigger below is a backstop; the primary control is a database role with no
-- UPDATE or DELETE on this table.
create table moderation_action (
  id             bigserial   primary key,
  actor          bigint      not null references account(id),
  action         text        not null,
  subject_kind   text        not null,
  subject_id     text        not null,
  rationale      text        not null,
  supersedes     bigint      references moderation_action(id),
  public         boolean     not null default true,
  created_at     timestamptz not null default now()
);
create index moderation_action_subject_idx on moderation_action(subject_kind, subject_id);

create or replace function moderation_action_is_append_only() returns trigger
language plpgsql as $$
begin
  raise exception 'moderation_action is append-only; record a superseding row instead';
end;
$$;

create trigger moderation_action_no_update
  before update or delete on moderation_action
  for each row execute function moderation_action_is_append_only();

-- Intent, not installs. The site does not serve the files, so it can count outbound clicks and
-- API resolutions and nothing more — labelled honestly rather than presented as downloads.
create table resolution_event (
  subject_kind text        not null,
  subject_id   text        not null,
  kind         text        not null,
  occurred_at  timestamptz not null default now(),

  constraint resolution_event_kind_valid check (kind in ('outbound_click', 'api_resolve'))
);
create index resolution_event_subject_idx on resolution_event(subject_kind, subject_id, occurred_at);

commit;
