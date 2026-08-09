-- Bugs in the site itself.
--
-- Distinct from `report`, which is about content: a mod that ships malware, a listing that stole
-- somebody's work. Those go to moderators to judge. A bug goes to whoever maintains the site to
-- fix, and the two queues want different fields, different states, and different people looking
-- at them. Folding them together would mean a moderator triaging "the search box is broken"
-- between takedown requests.
--
-- The reporter is told when it is dealt with. That is the whole reason this is a table rather
-- than a mailto: link. Somebody who takes the trouble to describe a bug has entered into a small
-- correspondence, and the ordinary experience of reporting one is never hearing anything again.
--
-- No BEGIN/COMMIT here: apply.sh runs each file under --single-transaction.

create table if not exists bug_report (
  id          bigserial   primary key,
  reporter    bigint      references account(id) on delete set null,
  summary     text        not null,
  detail      text,

  -- Where they were when it happened. Filled in by the page rather than typed, because nobody
  -- remembers the URL and "it was on some mod page" costs an hour of guessing.
  page        text,

  state       text        not null default 'open',

  -- What the reporter is told. Written by staff when they close it, and shown once on the
  -- reporter's next visit.
  resolution  text,

  created_at  timestamptz not null default now(),
  resolved_at timestamptz,
  resolved_by bigint      references account(id) on delete set null,

  -- When the reporter acknowledged the outcome. Null while the news is still waiting for them.
  seen_at     timestamptz,

  constraint bug_report_state_valid
    check (state in ('open', 'fixed', 'known', 'declined', 'duplicate')),

  -- A closed report has a time and an answer; an open one has neither. Enforced because the
  -- notification reads these three columns and a half-filled row would tell somebody their bug
  -- was dealt with while saying nothing about how.
  constraint bug_report_resolution_complete
    check ((state = 'open' and resolved_at is null and resolution is null)
        or (state <> 'open' and resolved_at is not null and resolution is not null)),

  constraint bug_report_summary_length check (length(summary) between 3 and 200),
  constraint bug_report_detail_length check (detail is null or length(detail) <= 4000)
);

-- The queue: open first, oldest first, which is the order somebody works through them in.
create index if not exists bug_report_queue_idx
  on bug_report(created_at) where state = 'open';

-- The notification lookup, which runs on every page render for a signed-in visitor and must
-- therefore be cheap and answer "nothing" quickly.
create index if not exists bug_report_unseen_idx
  on bug_report(reporter) where state <> 'open' and seen_at is null;
