-- Development seed data.
--
-- Deliberately includes the awkward cases rather than only happy ones: a yanked release, a
-- quarantined artifact, a failed validation, a deprecated listing with a successor, and an asset
-- id collision between two mods. Those are the states the UI most needs to get right, and a seed
-- of nothing but healthy mods lets every one of them ship broken.

begin;

insert into account (id, handle, display_name, github_login, site_role)
values (1, 'maxi', 'Maxi', 'Maximilian-Nesslauer', 'user')
on conflict do nothing;
select setval('account_id_seq', 10);

insert into mod (id, id_lower, type, name, abstract, description, license, tags, links, created_by) values
  ('AdvancedFlightComputer', 'advancedflightcomputer', 'mod', 'Advanced Flight Computer',
   'Extra maneuver planning tools for Kitten Space Agency.',
   'Adds a maneuver node editor, a delta-v budget readout and a transfer window planner.',
   'MIT', '{control,ui}',
   '{"forums":"https://forums.ahwoo.com/threads/advanced-flight-computer.783/","repository":"https://github.com/example/afc"}',
   1),
  ('OuterPlanets', 'outerplanets', 'mod', 'Outer Planets',
   'Adds Persephone and its moons to the outer system.',
   null, 'CC-BY-4.0', '{planets,systems}',
   '{"forums":"https://forums.ahwoo.com/threads/outer-planets.512/"}', 1),
  ('StarMap', 'starmap', 'mod-loader', 'StarMap',
   'The loader every KSA code mod depends on.', null, 'MIT', '{tools}',
   '{"forums":"https://forums.ahwoo.com/threads/starmap.101/"}', 1),
  ('KittenExtensions', 'kittenextensions', 'mod', 'Kitten Extensions',
   'Shared patching utilities used by several other mods.', null, 'MIT', '{tools}',
   '{"forums":"https://forums.ahwoo.com/threads/kitten-extensions.204/"}', 1),
  ('LegacyFuelTanks', 'legacyfueltanks', 'mod', 'Legacy Fuel Tanks',
   'Superseded parts pack, kept for existing saves.', null, 'MIT', '{parts}',
   '{"forums":"https://forums.ahwoo.com/threads/legacy-fuel-tanks.99/"}', 1),
  ('ModernFuelTanks', 'modernfueltanks', 'mod', 'Modern Fuel Tanks',
   'The continuation of Legacy Fuel Tanks.', null, 'MIT', '{parts}',
   '{"forums":"https://forums.ahwoo.com/threads/modern-fuel-tanks.100/"}', 1);

-- Deprecation with a successor: a renamed mod is unavoidably a different mod, because the id is
-- the folder name, so without this pointer every rename strands its users on a dead id.
update mod set status = 'deprecated', superseded_by = 'ModernFuelTanks' where id = 'LegacyFuelTanks';

insert into mod_maintainer (mod_id, account_id, role)
select id, 1, 'owner' from mod;

insert into mod_release
  (mod_id, version, version_sort, release_status, released_at, provider, provider_tag,
   game_min_revision, game_max_revision, game_min_display, game_max_display,
   loader_id, loader_min, install_root, install_derived, install_size,
   listing_snapshot, validation_state, availability, last_verified_at, yanked_at, yanked_reason)
values
  ('AdvancedFlightComputer', '1.2.0', '\x00000001000000020000000001'::bytea, 'stable',
   now() - interval '3 days', 'github', 'v1.2.0', 5117, null, '2026.8.3.5117', null,
   'StarMap', '0.4.5', 'AdvancedFlightComputer', true, 329449,
   '{}'::jsonb, 'passed', 'verified', now() - interval '1 day', null, null),

  -- A yanked release: the author's statement about one build, distinct from deprecation.
  ('AdvancedFlightComputer', '1.1.0', '\x00000001000000010000000001'::bytea, 'stable',
   now() - interval '20 days', 'github', 'v1.1.0', 5056, null, '2026.7.1.5056', null,
   'StarMap', '0.4.5', 'AdvancedFlightComputer', true, 320000,
   '{}'::jsonb, 'passed', 'verified', now() - interval '2 days',
   now() - interval '18 days', 'Maneuver nodes could corrupt a save on load. Use 1.2.0.'),

  -- Passed with warnings, and the bytes have since diverged from what was imported.
  ('OuterPlanets', '0.3.1', '\x00000000000000030000000101'::bytea, 'stable',
   now() - interval '10 days', 'github', 'v0.3.1', 5000, 5117, '2026.6.2.5000', '2026.8.3.5117',
   null, null, 'OuterPlanets', true, 88400000,
   '{}'::jsonb, 'passed_warnings', 'diverged', now() - interval '6 hours', null, null),

  -- Quarantined: the shape a compromised release makes.
  ('KittenExtensions', '0.4.0', '\x00000000000000040000000001'::bytea, 'stable',
   now() - interval '40 days', 'github', 'v0.4.0', 5000, null, '2026.6.2.5000', null,
   'StarMap', '0.4.0', 'KittenExtensions', true, 51200,
   '{}'::jsonb, 'passed', 'quarantined', now() - interval '1 hour', null, null),

  ('StarMap', '0.4.6', '\x00000000000000040000000601'::bytea, 'stable',
   now() - interval '6 days', 'github', 'v0.4.6', 5000, null, '2026.6.2.5000', null,
   null, null, 'StarMap', true, 2400000,
   '{}'::jsonb, 'passed', 'verified', now() - interval '1 day', null, null),

  ('LegacyFuelTanks', '2.0.0', '\x00000002000000000000000001'::bytea, 'stable',
   now() - interval '200 days', 'github', 'v2.0.0', 4800, 5056, '2026.5.1.4800', '2026.7.1.5056',
   null, null, 'LegacyFuelTanks', true, 12000000,
   '{}'::jsonb, 'passed', 'unavailable', now() - interval '3 days', null, null),

  ('ModernFuelTanks', '1.0.0', '\x00000001000000000000000001'::bytea, 'stable',
   now() - interval '30 days', 'github', 'v1.0.0', 5056, null, '2026.7.1.5056', null,
   null, null, 'ModernFuelTanks', true, 13500000,
   '{}'::jsonb, 'passed', 'verified', now() - interval '1 day', null, null);

-- A deterministic stand-in rather than a real digest: pgcrypto is not in the base image, and a
-- conditional on pg_extension does not help because Postgres resolves digest() at parse time
-- whatever the WHERE clause says. These are 32 bytes so the CHECK constraint holds, and they are
-- obviously fake so nobody mistakes seed data for a verified artifact.
insert into release_artifact (release_id, url, sha256, size, content_type)
select r.id,
       'https://github.com/example/' || lower(r.mod_id) || '/releases/download/v' || r.version || '/' || r.mod_id || '.zip',
       decode(lpad(to_hex(r.id), 64, 'a'), 'hex'),
       coalesce(r.install_size / 3, 100000),
       'application/zip'
from mod_release r;

-- An asset id collision between two mods. In game one of these silently loses, with no error and
-- no log line a player will find.
insert into release_asset_id (release_id, asset_id, xml_path)
select r.id, 'FuelTank', 'Assets/Tanks.xml' from mod_release r
where r.mod_id in ('LegacyFuelTanks', 'ModernFuelTanks');

insert into release_asset_id (release_id, asset_id, xml_path)
select r.id, 'OuterPlanets_Persephone', 'Assets/OuterPlanetsBodies.xml'
from mod_release r where r.mod_id = 'OuterPlanets';

insert into release_dependency (release_id, dep_id, kind, min_version, source)
select r.id, 'KittenExtensions', 'optional', null, 'derived'
from mod_release r where r.mod_id = 'AdvancedFlightComputer' and r.version = '1.2.0';

insert into release_console (release_id, hook, ordinal, command)
select r.id, 'onLoad', 0, 'camera map 2.88 0.47 3635076'
from mod_release r where r.mod_id = 'OuterPlanets';

insert into release_finding (release_id, stage, severity, code, message, path)
select r.id, 5, 'warning', 'KSAM-0503',
       '''Textures/Unused_Normal.ktx2'' is not declared in mod.toml and is not referenced from any declared XML, so the game will never load it.',
       'Textures/Unused_Normal.ktx2'
from mod_release r where r.mod_id = 'OuterPlanets';

insert into release_finding (release_id, stage, severity, code, message, path)
select r.id, 8, 'info', 'KSAM-0801',
       'This mod runs 1 developer console command(s) automatically. They are shown in full on the listing.',
       null
from mod_release r where r.mod_id = 'OuterPlanets';

insert into build (revision, version_string, released_on) values
  (4800, '2026.5.1.4800', date '2026-05-04'),
  (5000, '2026.6.2.5000', date '2026-06-11'),
  (5056, '2026.7.1.5056', date '2026-07-19'),
  (5117, '2026.8.3.5117', date '2026-08-01'),
  (5168, '2026.8.5.5168', date '2026-08-06');

commit;
