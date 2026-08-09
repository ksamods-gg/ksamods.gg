-- Proof that the person connecting a repository controls it (backend.md §5.2, §5.6).
--
-- Until now repo_link stored whatever the caller claimed. The unique constraint made it
-- first-come, which stops two listings fighting over one repository but does nothing about the
-- first claim being a stranger's. A repository link is the ownership proof for a listing, so an
-- unproven one is the whole trust model resting on nobody having tried.
--
-- The proof is a challenge the site issues and the claimant publishes in the repository, where
-- only somebody with write access can put it. That is deliberately not an OAuth token check: it
-- needs no stored access token, no app registration, and it works the same on every forge in the
-- allowlist. When a forge app is registered later, an installation id becomes a second accepted
-- proof and this column records which was used.
--
-- No BEGIN/COMMIT: db/apply.sh wraps each migration in --single-transaction.

alter table repo_link add column if not exists challenge   text;
alter table repo_link add column if not exists verified_at timestamptz;
alter table repo_link add column if not exists verified_by text;

-- Existing rows predate the check and were never proven. Marking them verified would launder
-- exactly the claim this migration exists to stop, so they stay pending and their owners
-- re-verify. There is no production data yet; if there were, this is still the right way round.
alter table repo_link drop constraint if exists repo_link_verified_has_method;
alter table repo_link add constraint repo_link_verified_has_method
  check ((verified_at is null and verified_by is null)
      or (verified_at is not null and verified_by in ('challenge', 'app_installation')));

-- The import job asks "which mods have a proven link and are due a look". Partial, because an
-- unverified link is not a candidate for anything.
create index if not exists repo_link_verified_idx on repo_link(last_seen_at)
  where verified_at is not null;

-- Webhook deliveries, kept so a redelivery is a no-op rather than a second import. The forge
-- retries on any non-2xx and duplicates are normal, not exceptional.
create table if not exists webhook_delivery (
  id          text        primary key,              -- the forge's delivery id
  provider    text        not null,
  event       text        not null,
  received_at timestamptz not null default now(),

  constraint webhook_delivery_provider_valid check (provider in ('github', 'gitlab', 'codeberg'))
);

-- Old deliveries are worthless once the retry window has passed; this index is what makes
-- trimming them cheap.
create index if not exists webhook_delivery_received_idx on webhook_delivery(received_at);

-- The worker claims jobs with FOR UPDATE SKIP LOCKED and needs to find a stuck one: a job whose
-- worker died mid-run holds `running` forever otherwise.
create index if not exists job_running_idx on job(locked_at) where state = 'running';
