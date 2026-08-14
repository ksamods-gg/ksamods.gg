-- 0010: the install descriptor (RFC 0035).
--
-- Where content is written, and what a person still has to do by hand. Until now the format could
-- say where content lived inside the archive and nothing about where it went on disk, which is
-- workable for a mod - the mods folder, named by the id, by universal convention - and impossible
-- for a loader. StarMap installs into a directory of its own and is pointed at the game through a
-- config file beside itself, and none of that is expressible or discoverable.
--
-- Stored as jsonb rather than as columns. The two sections are nested, optional, and shaped by an
-- RFC that expects to gain keys; a column per key would mean a migration per key, and the
-- alternative - a table per section - buys nothing when nothing ever queries inside them. What
-- guards the shape is InstallDescriptor.Check, which runs before anything is written and again
-- before anything is exported.

alter table mod add column if not exists install  jsonb;
alter table mod add column if not exists provides jsonb;

-- A pack ships no files, so it installs nothing of its own. A [provides] section belongs only to
-- a loader: it describes what that loader offers the content underneath it, which nothing else
-- has to offer. Both are validity rules in the RFC, so they are constraints here rather than
-- checks somebody has to remember to call.
alter table mod drop constraint if exists mod_install_not_on_pack;
alter table mod add constraint mod_install_not_on_pack
  check (install is null or type <> 'modpack');

alter table mod drop constraint if exists mod_provides_is_loader_only;
alter table mod add constraint mod_provides_is_loader_only
  check (provides is null or type = 'mod-loader');
