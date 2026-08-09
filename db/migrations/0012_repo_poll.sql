-- When each connected repository was last checked for new releases.
--
-- Release detection was webhook-only, and nothing in this project ever registers a webhook on an
-- author's repository: there is no app installation, the interface never mentions one, and the
-- endpoint is off unless a signing secret is configured. So in practice releases were detected
-- when somebody remembered to press Import, which is not detection.
--
-- This column is what lets a poller take turns fairly rather than re-checking whichever
-- repository it happens to see first.
--
-- No BEGIN/COMMIT here: apply.sh runs each file under --single-transaction.

alter table repo_link add column if not exists last_polled_at timestamptz;

-- Partial: the poller only ever considers proven links, so the index only needs to cover them.
create index if not exists repo_link_poll_idx
  on repo_link(last_polled_at nulls first)
  where verified_at is not null;
