-- Account deletion, as described in backend.md §4.3.
--
-- Deleting is anonymising, not removing the row. A person's mods, modlist versions and moderation
-- history are referenced by other rows and by other people's installs; dropping the account would
-- either cascade that away or leave dangling references. Neither is what "delete my account"
-- should mean on a site whose whole product is that published metadata stays resolvable.
--
-- So: the row survives with nothing identifying in it, the handle goes back to the pool, the
-- OAuth identities are removed so no provider can sign back into it, and every session is
-- revoked.
--
-- No BEGIN/COMMIT: db/apply.sh wraps each migration in --single-transaction.

alter table account add column if not exists deleted_at timestamptz;

-- A deleted account must not hold a name a living person could want, and must never be treated
-- as signed-in-able. Both are enforced here rather than trusted to the code that does it.
alter table account drop constraint if exists account_deleted_is_anonymous;
alter table account add constraint account_deleted_is_anonymous
  check (
    deleted_at is null
    or (github_login is null and discord_id is null and avatar_url is null and forums_url is null)
  );

-- Cheap, and it keeps "how many real accounts are there" from silently counting tombstones.
create index if not exists account_live_idx on account(id) where deleted_at is null;
