-- Repository ownership can also be proven with a topic.
--
-- The existing proof asks for a commit. That is a real cost for somebody whose repository has a
-- clean history, a protected default branch, or a review requirement on every change, and it is
-- a strange thing to ask of a person who only wants to list a mod. A topic is a settings change:
-- nothing enters git history, and there is nothing to remember to delete afterwards.
--
-- The challenge value does not change. 'ksamods-verify-' plus 32 hex characters is 47 long, all
-- lowercase alphanumerics and hyphens, which is a valid topic. The same string is either written
-- into the file or added as a topic, and the site accepts whichever it finds.
--
-- No BEGIN/COMMIT here: apply.sh runs each file under --single-transaction.

alter table repo_link drop constraint if exists repo_link_verified_has_method;

alter table repo_link add constraint repo_link_verified_has_method
  check ((verified_at is null and verified_by is null)
      or (verified_at is not null and verified_by in ('challenge', 'topic', 'app_installation')));
