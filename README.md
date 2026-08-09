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
this project follows them, which is what keeps the exported index consumable by other clients.

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
  KsaMods.Cli/             `ksamods validate`. The rules authors run locally.
  KsaMods.Web/             Blazor Web App frontend, Tailwind v4, shadcn-style components.
db/migrations/             Postgres schema.
db/seed-dev.sql            Development data, including the awkward states.
tests/KsaMods.Tests/       Fixture corpus and the guard tests.
```

`KsaMods.Validation` does no I/O at all: no `HttpClient`, no `File`, no database. That is what makes
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

Seed some data worth looking at, then start the frontend:

```bash
docker exec ksamods-pg psql -U ksamods -d ksamods -f /tmp/seed.sql   # db/seed-dev.sql

Api__BaseUrl='http://127.0.0.1:5199' dotnet run --project src/KsaMods.Web
```

The seed deliberately includes a yanked release, a quarantined artifact, a dead download link, a
deprecated listing with a successor, and an asset id collision between two mods. Those are the
states the UI most needs to get right, and a seed of nothing but healthy mods lets every one of
them ship broken.

## Deploying to Coolify

[`docker-compose.yaml`](docker-compose.yaml) is written for Coolify. It brings up the API, the
frontend and a one-shot migration; the database is yours to run, so create a Postgres resource
first and point this at it.

One variable is required, and deliberately has no default, because a default would mean every service
quietly coming up against a database that is not yours, and the first sign of it would be an empty
site. Compose refuses to start without it:

```
DATABASE_URL=postgres://user:password@host:5432/ksamods
```

Add `?sslmode=require` if your provider expects TLS. Percent-encode a password containing `@ : / #`.
Both this URL form and libpq's `Host=…;Username=…` form work, and the API normalises whichever it
gets ([`Database.Normalise`](src/KsaMods.Api/Data/Database.cs)), and so does the migration runner.

Coolify fills one value in automatically because it is declared with no value:

| Variable | What Coolify does |
|---|---|
| `SERVICE_FQDN_WEB_8080` | Assigns a domain, terminates TLS, routes it to the frontend |

Optional, for sign-in. Leave them unset and the auth routes are simply not mapped, which is a
working read-only site rather than a broken sign-in button:

```
GITHUB_CLIENT_ID       GITHUB_CLIENT_SECRET
DISCORD_CLIENT_ID      DISCORD_CLIENT_SECRET
```

The GitHub OAuth app's callback URL must be `https://<your-domain>/auth/github/callback`.

**Set `PUBLIC_BASE_URL` once you point a real domain at this** - `https://ksamods.gg`, no trailing
slash. It defaults to the domain Coolify generated, which is right until it isn't.

That value is what builds the OAuth `redirect_uri`, and it is configured rather than read off the
request on purpose. The API sits behind two proxies - Coolify's, then the frontend's - so its own
`Request.Host` is `api:8080`, and a `redirect_uri` derived from it points at a hostname that exists
only on a Docker network. Forwarded headers can carry the real host, but the `redirect_uri` has to
match the registered callback byte for byte, and staking that on a header surviving two hops is a
bad trade for one setting.

If it is missing, the API says so three ways rather than failing obscurely: a warning at startup, a
`500` naming the setting when a request arrives with an internal host, and the frontend hiding the
sign-in button entirely when no provider is configured.

**Only the frontend is exposed.** The API publishes no ports; the browser reaches it
through the frontend's own proxy. That is deliberate: it is what lets the session cookie stay
HttpOnly and `SameSite=Lax` with no CORS policy anywhere. It also means `docker compose up` locally
gives you nothing to curl; add a `ports:` mapping to the `web` service if you want that.

### Migrations

A one-shot `migrate` service runs before the API starts, against the same `DATABASE_URL`. It
records what it has applied in a `schema_migration` table, so re-running on every deploy is a
no-op, and adding `0002_*.sql` later applies only that file. It retries for a minute before giving
up, because a managed database may still be waking up when the deploy reaches it.

**The SQL is baked into an image rather than bind-mounted, and that is load-bearing.** Coolify runs
`docker compose` inside a helper container while talking to the host's Docker daemon, so a relative
bind mount like `./db:/db` resolves to a path that exists in the helper but not on the host. The
daemon then creates an empty directory there and the migration file is silently absent - the first
version of this compose file failed on Coolify for exactly that reason while working locally.

Migration files carry no `BEGIN`/`COMMIT`: `db/apply.sh` runs each under `--single-transaction` so
the schema change and its bookkeeping row commit together, and an explicit `COMMIT` inside the file
would end that transaction early.

### Verified

Built and run end to end: all three services reach healthy, `migrate` applies and records the
schema, the frontend serves its pages, the proxy reaches the API, and re-running `migrate` reports
`0001_initial.sql already applied`. The failure paths were tested too - an empty password and an
unreachable database both fail with a message that names the cause.

### Two things that had to change for this to work

**Forwarded headers.** Behind a TLS-terminating proxy both apps see plain HTTP on an internal
address. Left alone, `UseHttpsRedirection` redirects to https, the proxy forwards the retry as
http, and the browser loops; and the OAuth start endpoint builds its `redirect_uri` from
`Request.Scheme`, producing an `http://` callback that will not match what you registered.
`KnownIPNetworks` and `KnownProxies` must be *cleared* or the headers are silently ignored, since
the defaults trust only loopback and in a container the proxy is always another address.

**Healthchecks probe from inside the app.** The runtime images are chiselled, with no shell, no
curl and no wget, so `HealthProbe.cs` gives each app a `--healthcheck` argument that requests its own
`/health` and exits 0 or 1. Adding curl to the image to avoid this would mean shipping a binary,
and an attack surface, for one request the app can make itself.

## The frontend

Blazor Web App, server-rendered by default so mod pages are indexable, with interactive
rendering only where a page needs it. Tailwind CSS v4 with a shadcn-style token layer: every
colour is a CSS variable, so the dark theme is a token swap rather than a `dark:` prefix on every
element. Components live in `Components/Ui` and are owned outright, so there is no component
dependency to track.

**The browser only ever sees one origin.** `Services/ApiProxy.cs` forwards `/api` and `/auth` to
the .NET API, so the API keeps its HttpOnly, `SameSite=Lax` session cookie with no CORS policy
and no token handling in JavaScript.

The `NewMod` form validates ids against `KsaMods.Metadata.ContentId`, literally the same code the
API enforces, via a project reference, so the two cannot drift.

Tailwind is built by an MSBuild target before compile, so a fresh clone never serves stale CSS:

```bash
cd src/KsaMods.Web && npm install     # once
dotnet build src/KsaMods.Web          # runs `npm run build:css` automatically
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

Building the image is not enough. **The runtime flags are the sandbox**, and a Dockerfile cannot
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
- **Reachability is two hops.** Textures and meshes are correctly absent from `mod.toml`, because they
  are referenced from inside the declared XML. Only files reachable from *neither* are warned
  about. A validator that warns on everything not in `mod.toml` fires on the correct shape of
  every content mod, and the warning authors ignore is worth less than no warning at all.

Everything a mod ships is also recorded on first contact: asset ids, StarMap dependencies,
assemblies, `[console]` blocks. The site does not keep the archive, so a fact not extracted now
may be unrecoverable once the asset URL rots.

## What the resolver does

`POST /api/v1/resolve` takes a wanted set and a game revision and returns an ordered install plan
or a structured explanation of why the set is unsatisfiable. It also returns **asset id
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
the CDN; a public git repository survives because it gets forked, and forks happen before the
outage, not after.

Output is deterministic: stable ordering, LF endings, no BOM, timestamps from the input rather
than the clock. A run that changes nothing produces no commit, or the history becomes noise.

## Status

Phases 1 to 5 of [backend.md §18](docs/backend.md) are implemented, plus a frontend, with 411
tests passing.

**Verified end-to-end:** the validation pipeline via the CLI and inside the hardened container;
the migrations applied to a real Postgres 17 with every constraint checked individually
(case-insensitive id uniqueness, single-owner-per-mod, the append-only moderation trigger,
`superseded_by` requiring deprecation); the API serving live reads, resolve, collisions, 401 on
unauthenticated writes, and its security headers; the import path from webhook to job to
container to database; re-verification returning `verified`, `quarantined` and `unavailable`
against a real forge; and the frontend rendering the full stack in a real browser, in both
themes, with zero failed requests.

**Not yet built:** the modlist editor, forge adapters beyond GitHub, and an app-installation
proof alongside the challenge and topic ones. The modlist API and schema exist; only the pages
are missing, which is why nothing in the interface offers modlists yet.

### One deployment note

`CompressionEnabled` is `false` in `KsaMods.Web.csproj`, and it has to stay that way until the SDK
bug it works around is fixed. The SDK writes precompressed static assets with a literal `{0}` left
in the filename, so at runtime the app advertises a gzip variant, fails to find it, and answers a
browser with an empty `200`. Stylesheets then parse to zero rules and `blazor.web.js` aborts, with
nothing in any log, because the status is `200` and `curl` (which sends no `Accept-Encoding`) sees
the correct file. Compression belongs at the CDN or reverse proxy anyway.
