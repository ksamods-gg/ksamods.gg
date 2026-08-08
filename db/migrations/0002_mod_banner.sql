-- Mod banner images.
--
-- The site does not host files, and a banner is no exception: this is a URL pointing at an image
-- the author already hosts, exactly like the release assets. We store the link and nothing else,
-- so a banner cannot become the thing that makes us a file host by accident.
--
-- https only, because the page that shows it is https and a mixed-content image just fails to
-- load. Length is capped so a pathological URL cannot bloat every search response that returns
-- this row.

begin;

alter table mod add column banner_url text;

alter table mod add constraint mod_banner_url_valid
  check (
    banner_url is null
    or (banner_url ~ '^https://' and length(banner_url) <= 2048)
  );

commit;
