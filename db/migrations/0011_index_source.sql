-- 0011: the community index becomes the catalogue.
--
-- KSAModding/content-index holds the authored TOML and KSAModding/content-index-releases holds
-- the stamped releases. Both are arbitrated upstream, so this site stops being a source of
-- listings and becomes a reader of them.
--
-- The schema is not replaced. Every read path - search, the mod page, collisions, resolve, the
-- API - already works against these tables, and pointing them at a different store would be a
-- rewrite of everything to change where three files come from. So the tables stay and the sync
-- fills them; what this migration adds is only what upstream rows need and local rows never did.

-- Where a row came from, and therefore who may change it. 'index' rows are rewritten wholesale by
-- the sync on every run, so an edit made here would silently vanish on the next pull - which is
-- why the API refuses one rather than accepting it and losing it.
alter table mod add column if not exists source text not null default 'local';

alter table mod drop constraint if exists mod_source_valid;
alter table mod add constraint mod_source_valid check (source in ('local', 'index'));

alter table mod_release add column if not exists source text not null default 'local';

alter table mod_release drop constraint if exists mod_release_source_valid;
alter table mod_release add constraint mod_release_source_valid check (source in ('local', 'index'));

-- Authors as names, not accounts.
--
-- A local listing gets its authors from mod_maintainer joined to account, because those people
-- signed in here and the row is a permission as much as a credit. An upstream author is a string
-- in somebody else's TOML with no account on this site and no reason to have one, and inventing
-- placeholder accounts to hold a credit would put rows in the account table that can never sign
-- in, be suspended, or be deleted. The read paths coalesce: this column when it is set, the
-- maintainer join otherwise.
alter table mod add column if not exists authors text[];

-- An upstream listing has no creator here. Nullable rather than pointed at a synthetic system
-- account, so "nobody on this site created it" is stated rather than impersonated.
alter table mod alter column created_by drop not null;

alter table mod drop constraint if exists mod_local_has_creator;
alter table mod add constraint mod_local_has_creator
  check (source = 'index' or created_by is not null);

-- index-status.toml carries 'disputed', which our listing_state had no room for: the listing
-- stays in the catalogue whole and the client warns, which is neither listed-and-fine nor
-- withdrawn. Delisted maps onto the state we already had.
alter table mod drop constraint if exists mod_listing_state_valid;
alter table mod add constraint mod_listing_state_valid
  check (listing_state in ('listed', 'unlisted', 'delisted', 'taken_down', 'disputed'));

-- Why the index says a listing is delisted or disputed, shown to a reader as the index's own
-- voice rather than the author's.
alter table mod add column if not exists index_status_reason text;
alter table mod add column if not exists index_status_since  timestamptz;

-- Which commit of each repository the catalogue currently reflects. One row, so a sync can tell
-- "nothing changed upstream" without walking every file, and an operator can see how current the
-- catalogue is without reading logs.
create table if not exists index_sync (
  id                 boolean     primary key default true,
  authored_commit    text,
  generated_commit   text,
  synced_at          timestamptz not null default now(),
  listings           integer     not null default 0,
  releases           integer     not null default 0,
  last_error         text,

  constraint index_sync_single_row check (id)
);

insert into index_sync (id) values (true) on conflict (id) do nothing;

create index if not exists mod_source_idx on mod(source);
