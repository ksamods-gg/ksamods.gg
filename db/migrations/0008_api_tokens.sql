-- API tokens (backend.md §4.1, §10.3).
--
-- Two kinds, one table, because they differ in what they claim rather than in how they are stored:
--
--   personal    - acts as the account that made it. Lifts the read quota and lets a script see
--                 that account's own non-public data: its drafts, its failed validation reports.
--   application - identifies a piece of software. Owned by an account so somebody is reachable,
--                 but claims no user identity: it lifts the quota and appears in the logs.
--
-- **Neither can write.** Anonymous reads stay open, so a token buys quota and visibility of your
-- own drafts, never the power to act. Publishing is a session, and automated publishing is the
-- webhook, which already exists - so nothing legitimate needs a write token, and a credential
-- sitting in a CI config cannot be spent on deleting an account or moderating.
--
-- Hashed, never stored. A leaked database gives an attacker no usable credential, exactly as with
-- sessions. Plain SHA-256 rather than a password KDF: these are 256 bits from a CSPRNG, not
-- human-chosen, so there is no dictionary to grind and the cost of bcrypt would buy nothing while
-- being paid on every request.
--
-- No BEGIN/COMMIT: db/apply.sh wraps each migration in --single-transaction.

create table if not exists api_token (
  id          bigserial   primary key,
  account_id  bigint      not null references account(id) on delete cascade,

  kind        text        not null,
  name        text        not null,                 -- the owner's label, for the revoke list

  -- The lookup key. Unique so a collision is a constraint violation rather than two accounts
  -- sharing a credential.
  token_hash  bytea       not null unique,

  -- First characters of the secret, kept in clear so the owner can tell two tokens apart in a
  -- list. Short enough to be useless on its own.
  prefix      text        not null,

  created_at  timestamptz not null default now(),
  expires_at  timestamptz,
  revoked_at  timestamptz,

  -- Written at most once a minute, not per request: a token used by a polling client would
  -- otherwise be one row update per read, which is the read path paying for an audit field.
  last_used_at timestamptz,

  constraint api_token_kind_valid check (kind in ('personal', 'application')),
  constraint api_token_hash_length check (octet_length(token_hash) = 32),
  constraint api_token_name_present check (length(btrim(name)) between 1 and 64)
);

-- The hot path: resolve a presented secret to a live token. Partial, because a revoked one is
-- never a candidate and the index should not carry them.
create index if not exists api_token_live_idx on api_token(token_hash) where revoked_at is null;

-- The owner's list, newest first.
create index if not exists api_token_account_idx on api_token(account_id, created_at desc);
