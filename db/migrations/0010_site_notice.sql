-- The site notice, moved out of configuration and into the database.
--
-- The banner component has existed since the frontend did, reading its text from configuration.
-- That meant turning one on was a deploy, or at best an environment change and a restart, which
-- is the wrong shape for the thing it is: an outage message is needed at the moment nobody wants
-- to be running a deploy.
--
-- One row, enforced rather than assumed. A settings table that permits many rows grows a "which
-- one is live" question and then a bug where two are.
--
-- No BEGIN/COMMIT here: apply.sh runs each file under --single-transaction.

create table if not exists site_notice (
  id          smallint    primary key default 1,
  message     text        not null default '',
  variant     text        not null default 'info',
  link_text   text,
  link_href   text,
  dismissible boolean     not null default true,
  updated_at  timestamptz not null default now(),
  updated_by  bigint      references account(id) on delete set null,

  constraint site_notice_single_row check (id = 1),
  constraint site_notice_variant_valid check (variant in ('info', 'warning', 'error')),

  -- A link with no address is a dead anchor; an address with no text is a link that says nothing.
  constraint site_notice_link_complete
    check ((link_text is null or link_text = '') = (link_href is null or link_href = '')),

  -- Rendered as an anchor on every page, so the same rule as every other user-supplied link.
  constraint site_notice_link_https
    check (link_href is null or link_href = '' or link_href ~ '^https://')
);

insert into site_notice (id) values (1) on conflict (id) do nothing;
