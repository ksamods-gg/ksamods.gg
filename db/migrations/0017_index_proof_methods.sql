-- 0017: record the two proofs RFC 0038 added.
--
-- The ownership check learned to accept the community index's own proofs: the topic
-- `ksa-index-<login>`, and the marker file `.github/ksa-content-index.toml`. It writes
-- verified_by = 'index-topic' or 'index-marker', and neither was in the allowed set - so a proof
-- that succeeded failed at the constraint, and the author saw a 500 for having done the right
-- thing. Nothing was mis-recorded, because nothing was recorded at all.
--
-- Kept distinct from 'topic' rather than folded into it. Both are topics, but they are different
-- claims: 'topic' is this site's per-listing secret, placed for us and meaningless anywhere else,
-- while 'index-topic' is a stable public string naming an account, set once for the community
-- index and merely also accepted here. When somebody asks later how a listing was proven, those
-- are two different answers.

alter table repo_link drop constraint if exists repo_link_verified_has_method;

alter table repo_link
    add constraint repo_link_verified_has_method
    check (
        (verified_at is null and verified_by is null)
        or (verified_at is not null
            and verified_by in ('challenge', 'topic', 'staff', 'app_installation', 'owner',
                                'index-topic', 'index-marker'))
    );
