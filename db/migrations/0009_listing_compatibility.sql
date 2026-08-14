-- 0009: the authored compatibility bound.
--
-- RFC 0031 makes game_min a required field on the authored document, and until now the schema
-- had nowhere to put it: the bound existed only on mod_release, where nothing ever wrote it.
-- The consequence is not cosmetic. RFC 0017 reads a missing lower bound as Unknown rather than
-- "any", so every release we published evaluated as Unknown against every install, which is the
-- one thing the compatibility work exists to avoid.
--
-- The bound belongs on the listing because that is where the author states it once. A release
-- inherits it at stamp time and may narrow it afterwards through the amendment class, so the
-- release columns stay and this is a second, lower-precedence source.

alter table mod add column if not exists game_min_display  text;
alter table mod add column if not exists game_min_revision integer;
alter table mod add column if not exists game_max_display  text;
alter table mod add column if not exists game_max_revision integer;

-- A month bound ("2026.7") resolves to a revision only once the month is over, so display
-- without revision is a legitimate state and not a broken row. Revision without display is not:
-- it would print nothing to a human while ordering as though it had.
alter table mod drop constraint if exists mod_game_min_has_display;
alter table mod add constraint mod_game_min_has_display
  check (game_min_revision is null or game_min_display is not null);

alter table mod drop constraint if exists mod_game_max_has_display;
alter table mod add constraint mod_game_max_has_display
  check (game_max_revision is null or game_max_display is not null);

alter table mod drop constraint if exists mod_game_range_ordered;
alter table mod add constraint mod_game_range_ordered
  check (game_max_revision is null or game_min_revision is null
         or game_max_revision >= game_min_revision);
