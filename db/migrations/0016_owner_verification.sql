-- Lets a repository be verified by the fact that the person connecting it owns the account it
-- sits under.
--
-- Signing in with GitHub proves you control an account. It does not, on its own, prove you control
-- a repository, which is why connecting an identity never used to shorten the proof: otherwise
-- anybody with an account could claim RocketWerkz/KSA.
--
-- There is one case where the sign-in genuinely is the proof, and the site was making people jump
-- through the topic dance for it anyway. A repository at github.com/<login>/<name> is owned by
-- that account. If the account that connected it is that account, the claim is already settled and
-- there is nothing left to demonstrate.
--
-- Matched on the forge's numeric user id, not on the login. GitHub logins can be changed and a
-- freed-up one can be claimed by somebody else, so a string comparison would let a new holder of
-- an old name inherit a proof they never earned. Ids are stable for the life of the account.
--
-- Organisation-owned repositories are deliberately not covered. Membership of an org is not
-- ownership of its repositories, and reading it would need an OAuth scope this site promises on
-- its sign-in page not to ask for. Those keep the topic.
alter table repo_link
    drop constraint if exists repo_link_verified_has_method;

alter table repo_link
    add constraint repo_link_verified_has_method
    check (
        (verified_at is null and verified_by is null)
        or (verified_at is not null
            and verified_by in ('challenge', 'topic', 'staff', 'app_installation', 'owner'))
    );
