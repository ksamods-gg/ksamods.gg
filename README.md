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

## Status

Phase 1 of [backend.md §18](docs/backend.md), partially complete.

**Working:** metadata types, the validation pipeline, the fixture corpus and guard tests, the
container and its Dockerfile, the SSRF-guarded fetcher, container orchestration, the CLI, and the
Postgres schema.

**Not yet built:** the API, auth, modlists, the exporter and its git mirror, and the resolver.
