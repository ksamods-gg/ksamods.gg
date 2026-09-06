# ksamods_gg

To install dependencies:

```bash
bun install
```

To run:

```bash
bun run index.ts
```

This project was created using `bun init` in bun v1.4.2. [Bun](https://bun.com) is a fast all-in-one JavaScript runtime.

## Deploying (Coolify and Docker)

One `Dockerfile` with two targets. Two Coolify applications, both with the
**Dockerfile** build pack, **Base Directory** `/` and **Dockerfile Location**
`/Dockerfile`, differing only in their build stage target.

|                            | Server           | Web app          |
| -------------------------- | ---------------- | ---------------- |
| Docker Build Stage Target  | `server`         | `app`            |
| Port                       | 3000             | 3000             |
| Domain                     | `api.ksamods.gg` | `dev.ksamods.gg` |

The build context is the repository root, not a package directory: this is a Bun
workspace, so the lockfile and both manifests have to be visible to the install.

The two domains have to share a registrable domain. The session cookie is
`SameSite=Lax`, so an app on a different apex than the API would never send it
and the site would look permanently signed out.

`NEXT_PUBLIC_SERVER_URL` needs **Build Variable** enabled, so Coolify passes it
as a build argument. Next inlines it into the client bundle, so setting it only
at runtime leaves the browser talking to localhost, and changing it later is a
rebuild rather than a restart. Everything else in
`packages/server/.env.example` is runtime only.

`BETTER_AUTH_URL` is the server origin and `APP_URL` is the site origin; they
have to be the real public URLs or CORS and the OAuth callbacks fail. Auth is
mounted at `/auth`, so a callback reads as
`https://api.ksamods.gg/auth/callback/discord`.

The server target runs `prisma migrate deploy` before it serves, so a deploy
applies pending migrations first. Postgres is a separate Coolify resource that
`DATABASE_URL` points at. The `docker-compose.yml` in `packages/server` is for
local development only.

Both images are around 2 GB, almost all of it the workspace install. If that
becomes a problem, the fix is `output: "standalone"` in `next.config.ts` plus a
slim runtime stage, rather than trimming the build.

To run either locally:

```bash
docker build --target server -t ksamods-server .
docker build --target app --build-arg NEXT_PUBLIC_SERVER_URL=https://api.ksamods.gg -t ksamods-app .
```

## License

[CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/). See [LICENSE](LICENSE).

Content indexed from [KSAModding/content-index](https://github.com/KSAModding/content-index) is not
covered by this license and stays under its own terms.
