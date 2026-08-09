-- Mod icons.
--
-- A banner is a wide strip at the top of a card; an icon is the square mark that identifies a
-- listing in a dense row, a search result, a modlist, or a client. They do different jobs, so this
-- is a second column rather than a crop of the first: cropping a 16:6 banner to a square gives you
-- the middle of somebody's screenshot, which identifies nothing.
--
-- Same rules as the banner, for the same reasons: a URL to an image the author already hosts, https
-- only because the page showing it is https, and a length cap so a pathological URL cannot bloat
-- every search response carrying this row. The site does not host the file.
--
-- Nullable, and expected to stay null for most listings for a while. The frontend derives a mark
-- from the id when it is absent, so a listing without one never renders as a broken image or a
-- hole in a grid.

-- No BEGIN/COMMIT: db/apply.sh wraps each migration in --single-transaction.

alter table mod add column if not exists icon_url text;

alter table mod drop constraint if exists mod_icon_url_valid;
alter table mod add constraint mod_icon_url_valid
  check (
    icon_url is null
    or (icon_url ~ '^https://' and length(icon_url) <= 2048)
  );
