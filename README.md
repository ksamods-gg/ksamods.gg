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

## Deploying (Coolify and Docker Compose)

One Coolify application on the **Docker Compose** build pack, pointed at
`docker-compose.yaml` at the repository root. It runs three services: `db`,
`server` and `app`. Both application services build from the `Dockerfile` beside
it, each from its own target, with the repository root as the build context
because this is a Bun workspace and the lockfile has to be visible to the
install.

Give `server` and `app` a domain each in Coolify. Neither publishes a port; both
are reached through Coolify's proxy on container port 3000.

| Service  | Domain           |
| -------- | ---------------- |
| `server` | `api.ksamods.gg` |
| `app`    | `dev.ksamods.gg` |

The two domains have to share a registrable domain. The session cookie is
`SameSite=Lax`, so an app on a different apex than the API would never send it
and the site would look permanently signed out.

Set these in Coolify's environment variables; the compose file substitutes them
and refuses to start if a required one is missing:

| Variable                                                | Notes                            |
| ------------------------------------------------------- | -------------------------------- |
| `POSTGRES_PASSWORD`                                      | required                         |
| `BETTER_AUTH_SECRET`                                     | required, `openssl rand -base64 32` |
| `BETTER_AUTH_URL`                                        | the server's public origin       |
| `APP_URL`                                                | the site's public origin         |
| `NEXT_PUBLIC_SERVER_URL`                                 | the server's public origin, used at build and runtime |
| `DISCORD_*`, `GITHUB_*`, `STEAM_API_KEY`, `ADMIN_DISCORD_IDS` | optional, see `packages/server/.env.example` |

`NEXT_PUBLIC_SERVER_URL` is passed as a build argument as well, because Next
inlines it into the client bundle. Changing it is a rebuild, not a restart.

Auth is mounted at `/auth`, so an OAuth callback reads as
`https://api.ksamods.gg/auth/callback/discord`.

The server applies pending migrations before it serves, and waits on the
database's healthcheck first, so a cold start does not crash loop. Postgres data
lives in the `pgdata` volume. The compose file in `packages/server` is a
separate, local-development-only Postgres.

The app reaches the server over its public URL even for server-side rendering,
which means SSR traffic leaves and re-enters through the proxy. That is fine,
but if Coolify's proxy ever fails to resolve its own domain from inside a
container, this is the thing that breaks.

To run the whole stack locally:

```bash
docker compose up --build
```

## License

[CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/). See [LICENSE](LICENSE).

Content indexed from [KSAModding/content-index](https://github.com/KSAModding/content-index) is not
covered by this license and stays under its own terms.
