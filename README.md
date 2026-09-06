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

## Deploying (Coolify and Railpack)

Two applications, both pointed at this repository with **Base Directory** `/` and
the **Railpack** build pack. They differ only in which config file they use,
selected by a `RAILPACK_CONFIG_FILE` build variable.

|                        | Server                 | Web app             |
| ---------------------- | ---------------------- | ------------------- |
| `RAILPACK_CONFIG_FILE` | `railpack-server.json` | `railpack-app.json` |
| Internal port          | 3000                   | 3000                |

`RAILPACK_CONFIG_FILE` needs **Build Variable** enabled. Neither file is named
`railpack.json`, so a missing variable fails the build instead of quietly
shipping the wrong package.

Both builds install the whole workspace and run `prisma generate`: the app
imports the server router for its types, so the generated client has to exist
before `next build` typechecks.

`NEXT_PUBLIC_SERVER_URL` also needs **Build Variable** enabled. Next inlines it
into the client bundle, so setting it only at runtime leaves the browser talking
to localhost.

Everything else in `packages/server/.env.example` is runtime only.
`BETTER_AUTH_URL` is the server origin and `APP_URL` is the site origin; they
have to be the real public URLs or CORS and the OAuth callbacks fail.

The server runs `prisma migrate deploy` on start, so a deploy applies pending
migrations before serving. Postgres is a separate Coolify resource that
`DATABASE_URL` points at. The `docker-compose.yml` in `packages/server` is for
local development only.

## License

[CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/). See [LICENSE](LICENSE).

Content indexed from [KSAModding/content-index](https://github.com/KSAModding/content-index) is not
covered by this license and stays under its own terms.
