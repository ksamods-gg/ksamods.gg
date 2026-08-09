-- Lets a moderator set aside a validator warning on one release of one mod.
--
-- Validators are wrong sometimes. A heuristic that fires on the correct shape of a legitimate mod
-- puts a warning on somebody's release that nobody can clear, and the author has no way to answer
-- it: the finding is our verdict about their archive, recorded at import. Without this the only
-- remedies are to stop emitting the check for everybody or to leave the warning standing.
--
-- Keyed on the code, not on "all warnings for this version". Codes are the stable contract, and a
-- blanket rule would also swallow the next warning to appear on that release, which is the one
-- worth reading. Suppressing everything is still available as an action, and it writes one row per
-- code so it stays legible afterwards.
--
-- Errors are deliberately not suppressible, and that is enforced at the endpoint rather than here
-- because severity lives on release_finding and can in principle be re-recorded by a re-import. A
-- constraint that reads the other table would go stale; the check in front of the write does not.
create table release_finding_suppression (
    release_id     bigint      not null references mod_release(id) on delete cascade,
    code           text        not null,

    -- Required. A suppression with no reason is indistinguishable from a mistake six months later,
    -- and this table exists precisely for the cases somebody has to justify.
    reason         text        not null,

    suppressed_by  bigint      not null references account(id),
    suppressed_at  timestamptz not null default now(),

    primary key (release_id, code),

    constraint release_finding_suppression_reason_present
        check (length(btrim(reason)) between 1 and 500)
);

comment on table release_finding_suppression is
    'Validator findings a moderator has set aside on one release. The finding itself is never '
    'deleted: it stays in release_finding and stays in the API response, marked as suppressed '
    'with its reason, so setting one aside is a visible act rather than a disappearance.';
