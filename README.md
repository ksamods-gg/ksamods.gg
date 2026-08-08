# ksamods.gg

Mod site for [Kitten Space Agency](https://www.rocketwerkz.com/): a place to publish mods, and to
build modlists alone or with other people.

**The site never stores a mod file.** A release points at an asset on the author's own git forge.
The validation pipeline downloads that archive transiently, inspects it, and discards it; what
persists is a URL, a `sha256`, and everything the validator learned from the bytes.

## Documentation

| Document | What it covers |
|---|---|
| [docs/spec.md](docs/spec.md) | How KSA actually loads a mod, and what a conforming archive looks like. Every claim tagged `[CONFIRMED]`, `[CONVENTION]` or `[RFC]`. |
| [docs/plan.md](docs/plan.md) | Product plan: what the site is, what it promises, and what it deliberately does not do. |
| [docs/backend.md](docs/backend.md) | This codebase: architecture, schema, pipeline, security model, build order. |

The `content-manager-design` submodule is the [KSAModding](https://github.com/KSAModding) RFC
process. RFC 0017 (version ordering), 0025 (scope) and 0031 (metadata format) are Accepted and
this project follows them — that is what keeps the exported index consumable by other clients.

## Layout

```
src/
  KsaMods.Metadata/        RFC 0031 types, id rules, KSA + SemVer versions. No I/O.
  KsaMods.Validation/      The pipeline stages as pure functions over an archive.
  KsaMods.Validator.Host/  What runs inside the container. Reads /in, writes /out.
  KsaMods.Worker/          SSRF-guarded fetch, container orchestration.
  KsaMods.Resolver/        Dependency and conflict solving.
  KsaMods.Api/             ASP.NET Core. Everything user-facing.
  KsaMods.Exporter/        Builds the static index and pushes the git mirror.
  KsaMods.Cli/             `ksamods validate` — the rules authors run locally.
db/migrations/             Postgres schema.
tests/KsaMods.Tests/       Fixture corpus and the guard tests.
```

`KsaMods.Validation` does no I/O — no `HttpClient`, no `File`, no database. That is what makes
"the CLI and the server run identical rules" true by construction rather than by discipline.

## Build and test

```bash
dotnet build
dotnet test
```

## Run the API

```bash
docker run -d --name ksamods-pg -p 55432:5432 \
  -e POSTGRES_USER=ksamods -e POSTGRES_PASSWORD=ksamods -e POSTGRES_DB=ksamods \
  postgres:17-alpine

docker cp db/migrations/0001_initial.sql ksamods-pg:/tmp/0001.sql
docker exec ksamods-pg psql -U ksamods -d ksamods -v ON_ERROR_STOP=1 -f /tmp/0001.sql

ConnectionStrings__Postgres='Host=127.0.0.1;Port=55432;Database=ksamods;Username=ksamods;Password=ksamods' \
  dotnet run --project src/KsaMods.Api
```

Sign-in needs OAuth credentials; without them the auth routes are simply not mapped and
everything else works. Set `OAuth__GitHub__ClientId` / `OAuth__GitHub__ClientSecret` (and the
Discord equivalents) to enable them.

## Validate an archive locally

```bash
dotnet run --project src/KsaMods.Cli -- validate path/to/YourMod.zip
```

The mod id defaults to the archive's single top-level directory; pass `--id` to check it against
the id you actually claimed. Exit code is 0 for pass or pass-with-warnings, 1 for failure.

Sample output:

```
warning KSAM-0503: 'Textures/Orphan.ktx2' is not declared in mod.toml and is not referenced
from any declared XML, so the game will never load it. [Textures/Orphan.ktx2]
note KSAM-0801: This mod runs 1 developer console command(s) automatically.

OuterPlanets: passed with warnings (0 error(s), 1 warning(s))
  2 asset id(s), 0 declared dependency(ies), 0 assembly(ies), 1 KiB unpacked
```

## The validation container

Building the image is not enough — **the runtime flags are the sandbox**, and a Dockerfile cannot
express them. `KsaMods.Worker/ContainerRunner.cs` is what applies them, and
`ContainerFlagTests` is what stops them regressing.

```bash
docker build -f src/KsaMods.Validator.Host/Dockerfile -t ksamods/validator:dev .

docker run --rm \
  --network none --read-only --tmpfs=/tmp:rw,noexec,nosuid,size=512m \
  --user 64198:64198 --cap-drop ALL --security-opt no-new-privileges \
  --pids-limit=128 --memory=1g --memory-swap=1g --cpus=1.0 \
  -v "$PWD/archive.zip:/in/archive.zip:ro" -v "$PWD/out:/out" \
  ksamods/validator:dev YourModId
```

`--network none` is the one that matters most: the worker fetches the archive on the host, where
DNS and egress can be controlled, and the container that parses it can reach nothing at all.

## What the validator checks

Stages 3–8 of [backend.md §7.3](docs/backend.md). The one nobody else does is **stage 5**:

- Every path declared in `mod.toml` resolves to a real file.
- Every declared XML parses, with the right root element.
- **Reachability is two hops.** Textures and meshes are correctly absent from `mod.toml` — they
  are referenced from inside the declared XML. Only files reachable from *neither* are warned
  about. A validator that warns on everything not in `mod.toml` fires on the correct shape of
  every content mod, and the warning authors ignore is worth less than no warning at all.

Everything a mod ships is also recorded on first contact — asset ids, StarMap dependencies,
assemblies, `[console]` blocks. The site does not keep the archive, so a fact not extracted now
may be unrecoverable once the asset URL rots.

## What the resolver does

`POST /api/v1/resolve` takes a wanted set and a game revision and returns an ordered install plan
— or a structured explanation of why the set is unsatisfiable. It also returns **asset id
collisions within the resolved set**, so a client can warn before writing anything.

`KsaMods.Resolver` ships as a library as well as running behind the endpoint. The offline index
export means clients must be able to solve locally, so a server-only solver would merely
guarantee a second, subtly different implementation appears.

The returned order is what a client writes into `manifest.toml`. It is **not** a promise about
initialisation order: StarMap defers mods with unmet dependencies through its waiting graph and
reorders initialisation within the walk.

## The export and its git mirror

Every export run writes RFC 0031 documents and commits them to a public git repository. This is
required, not optional ([backend.md §12.1](docs/backend.md)): Postgres holds the record, so
without the mirror the catalogue does not outlive the service. A tarball behind a CDN dies with
the CDN; a public git repository survives because it gets forked — and forks happen before the
outage, not after.

Output is deterministic: stable ordering, LF endings, no BOM, timestamps from the input rather
than the clock. A run that changes nothing produces no commit, or the history becomes noise.

## Status

Phases 1–5 of [backend.md §18](docs/backend.md) are implemented, with 220 tests passing.

**Verified end-to-end:** the validation pipeline via the CLI and inside the hardened container;
the migrations applied to a real Postgres 17 with every constraint checked individually
(case-insensitive id uniqueness, single-owner-per-mod, the append-only moderation trigger,
`superseded_by` requiring deprecation); and the API serving live reads, resolve, collisions,
401 on unauthenticated writes, and its security headers.

**Not yet built:** the release-import worker loop that ties webhook → fetch → container →
database together (its pieces all exist and are tested separately), the periodic re-verification
job, forge adapters beyond GitHub, and the frontend.
