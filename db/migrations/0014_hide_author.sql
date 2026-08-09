-- Lets a listing be published without naming who published it.
--
-- Some people put their real name on an account and would rather it not sit at the top of a
-- catalogue entry, and some just do not want the two connected. Before this the only way to
-- publish without a name attached was a second account, which is worse for everybody: it splits
-- their listings, and it makes ownership transfers and takedowns land on a stranger.
--
-- A flag on the listing rather than on the account, because it is per-listing by nature. The same
-- person can want their name on one mod and not on another, and a per-account switch would make
-- that impossible without the second account this is meant to avoid.
--
-- The maintainer rows are untouched. Ownership still exists, moderators can still see it, and the
-- author still gets their own management screen. Hidden means hidden from readers, not anonymous
-- to us: a listing nobody is answerable for is not something we want in the catalogue.
alter table mod
    add column hide_author boolean not null default false;

comment on column mod.hide_author is
    'Suppresses the author on the public listing and on their public profile. Ownership is '
    'unchanged and still visible to moderators.';
