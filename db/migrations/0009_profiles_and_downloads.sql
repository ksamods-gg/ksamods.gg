-- Public profiles, and a download count that means something.
--
-- Profiles: an account already carries a handle, a display name and an avatar, all of which the
-- site shows. What it has no room for is the part a person writes themselves, so a listing's
-- author is a name and nothing else. bio and links give them somewhere to say who they are and
-- where else to find them.
--
-- Downloads: the site cannot count these honestly on its own. An outbound click is intent, not an
-- install, and PRODUCT.md says so in as many words: label it as such, or fetch the real number
-- from the forge. This is the second option. GitHub counts the times the asset itself was fetched
-- and returns it on the release listing the importer already reads, so the number stored here is
-- a measurement somebody else made of the actual file rather than a proxy we invented.
--
-- No BEGIN/COMMIT here: apply.sh runs each file under --single-transaction.

alter table account add column if not exists bio   text;
alter table account add column if not exists links jsonb not null default '{}'::jsonb;

-- Long enough to introduce yourself and say what you make, short enough that a profile stays a
-- profile rather than becoming a second mod page.
alter table account drop constraint if exists account_bio_length;
alter table account add constraint account_bio_length
  check (bio is null or length(bio) <= 600);

-- Per artifact, because that is the granularity the forge reports: one release can attach more
-- than one file, and only the one we recorded is the one being counted.
alter table release_artifact add column if not exists download_count integer;
alter table release_artifact add column if not exists counted_at     timestamptz;

alter table release_artifact drop constraint if exists release_artifact_download_count_sane;
alter table release_artifact add constraint release_artifact_download_count_sane
  check (download_count is null or download_count >= 0);
