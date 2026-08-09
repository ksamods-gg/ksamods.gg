-- A curated tag vocabulary (RFC 0031 §"tags", backend.md §3).
--
-- RFC 0031 defines the field as "free-form lowercase tags" and says a curated vocabulary "can come
-- later without a format change". This is that vocabulary, and the RFC's phrasing decides its
-- shape: it is *this index's* curation policy, not a change to the format.
--
-- Three consequences follow, and they are the whole design:
--
--   · mod.tags stays text[] of lowercase strings, and the export still emits a plain string array.
--     Nothing downstream has to learn a new type.
--   · The vocabulary is enforced where an author picks a tag - the write endpoints - and not by a
--     constraint on the column. §12 ingest will one day carry listings from elsewhere whose tags
--     are valid per the format and absent from our list; refusing those at the storage layer would
--     be asserting our policy as though it were the format's, and would make ingest impossible.
--   · Because the column stays permissive, the admin panel needs a way to see tags in use that are
--     not in the vocabulary, so curation catches up instead of blocking.
--
-- No BEGIN/COMMIT: db/apply.sh wraps each migration in --single-transaction.

create table if not exists tag (
  slug        text        primary key,
  label       text        not null,
  description text,

  state       text        not null default 'proposed',

  -- Who wanted it. Null for the tags seeded below, which predate the vocabulary and were nobody's
  -- proposal - they were simply in use.
  proposed_by bigint      references account(id) on delete set null,
  reason      text,                                   -- why the proposer thinks it is needed
  created_at  timestamptz not null default now(),

  reviewed_by bigint      references account(id) on delete set null,
  reviewed_at timestamptz,
  review_note text,                                   -- shown to the proposer on a rejection

  constraint tag_state_valid check (state in ('proposed', 'approved', 'rejected')),

  -- Lowercase, and the same shape as everything else people type at each other here: ASCII
  -- letters, digits and single hyphens. A tag is a filter in a URL and a word in a sentence, so
  -- it gets no room for spaces, case or punctuation to differ invisibly.
  constraint tag_slug_shape check (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
  constraint tag_slug_length check (length(slug) between 2 and 32),

  -- A decision has a decider. Without this a tag could sit approved with no record of who said so.
  constraint tag_reviewed_together
    check ((state = 'proposed' and reviewed_at is null)
        or (state <> 'proposed' and reviewed_at is not null))
);

create index if not exists tag_state_idx on tag(state, slug);

-- Seed the vocabulary from what is already in use, approved. The alternative - starting empty -
-- would make every existing listing's tags instantly non-conforming and the first admin's job a
-- data-entry exercise. Reviewed by nobody, on purpose: these were not decisions, and the null
-- reviewer says so rather than inventing an account that approved them.
insert into tag (slug, label, state, reviewed_at, review_note)
select distinct lower(t), lower(t), 'approved', now(),
       'In use before the vocabulary existed; adopted rather than decided.'
from mod, unnest(mod.tags) as t
where lower(t) ~ '^[a-z0-9]+(-[a-z0-9]+)*$'
  and length(lower(t)) between 2 and 32
on conflict (slug) do nothing;

-- Modlists carry tags too and draw from the same vocabulary; seeding both keeps one list rather
-- than two that drift.
insert into tag (slug, label, state, reviewed_at, review_note)
select distinct lower(t), lower(t), 'approved', now(),
       'In use before the vocabulary existed; adopted rather than decided.'
from modlist, unnest(modlist.tags) as t
where lower(t) ~ '^[a-z0-9]+(-[a-z0-9]+)*$'
  and length(lower(t)) between 2 and 32
on conflict (slug) do nothing;
