-- Staff can vouch for a repository link without the usual proof.
--
-- The challenge and topic proofs both answer one question: can this person write to that
-- repository. A moderator can answer it by other means, and often has to: an author who has gone
-- quiet, a forum thread that settles ownership, a handover nobody can complete because the
-- previous owner is unreachable. Without this the only path is refusing to help.
--
-- Recorded as its own method rather than written down as a challenge that never happened, so the
-- row says truthfully how the link came to be trusted and an audit can tell the two apart.
--
-- No BEGIN/COMMIT here: apply.sh runs each file under --single-transaction.

alter table repo_link drop constraint if exists repo_link_verified_has_method;

alter table repo_link add constraint repo_link_verified_has_method
  check ((verified_at is null and verified_by is null)
      or (verified_at is not null
          and verified_by in ('challenge', 'topic', 'staff', 'app_installation')));
