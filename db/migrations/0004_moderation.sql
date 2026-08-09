-- Moderation working state (backend.md §13).
--
-- The review queue itself is derived rather than stored: a release needs review when it ships a
-- DLL, carries a [console] block, is quarantined, or has an open report against it, and all four
-- are already answerable from existing tables. Storing a duplicate flag would be a second source
-- of truth that drifts the first time one of those conditions is added.
--
-- What cannot be derived is that a human has looked. That is what release_review records.
--
-- No BEGIN/COMMIT: db/apply.sh wraps each migration in --single-transaction.

create table if not exists release_review (
  release_id  bigint      primary key references mod_release(id) on delete cascade,
  reviewed_by bigint      not null references account(id),
  reviewed_at timestamptz not null default now(),
  outcome     text        not null,
  notes       text,

  constraint release_review_outcome_valid check (outcome in ('cleared', 'actioned'))
);

-- Who closed a report and when. The rationale lives in moderation_action, because that is the
-- record that has to survive in the export; this is just the pointer back.
alter table report add column if not exists resolved_by bigint references account(id);
alter table report add column if not exists resolved_at timestamptz;

alter table report drop constraint if exists report_resolution_is_complete;
alter table report add constraint report_resolution_is_complete
  check (
    (state = 'open' and resolved_at is null)
    or (state <> 'open' and resolved_at is not null)
  );

-- The queues an admin actually opens. Each is a small partial index rather than a scan of a table
-- that is mostly fine.
create index if not exists mod_release_quarantined_idx on mod_release(created_at desc)
  where availability = 'quarantined';

create index if not exists mod_release_needs_review_idx on mod_release(created_at desc)
  where validation_state in ('passed', 'passed_warnings');

create index if not exists job_dead_idx on job(created_at desc) where state = 'dead';

create index if not exists mod_withdrawn_idx on mod(updated_at desc)
  where listing_state in ('delisted', 'taken_down');

create index if not exists account_suspended_idx on account(suspended_at desc)
  where suspended_at is not null;
