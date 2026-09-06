# syntax=docker/dockerfile:1

# One file, two targets. Build with --target server or --target app; in Coolify
# that is the "Docker Build Stage Target" field, with the build context at the
# repository root because this is a Bun workspace and the lockfile lives there.

# Node and Bun both, deliberately. The app and the server run under Bun, but the
# Prisma CLI is a Node program with a node shebang, and `bun run` honours it.
FROM node:22-bookworm-slim AS base
COPY --from=oven/bun:1.4.2 /usr/local/bin/bun /usr/local/bin/bun
# bunx is a symlink to bun in the official image, so copying the binary alone
# leaves it missing.
RUN ln -s /usr/local/bin/bun /usr/local/bin/bunx
# Prisma probes for the openssl CLI and warns loudly on every command without
# it. node:slim ships libssl but not the binary.
RUN apt-get update \
  && apt-get install -y --no-install-recommends openssl \
  && rm -rf /var/lib/apt/lists/*
WORKDIR /app

# Dependencies on their own layer, so editing source does not reinstall them.
FROM base AS deps
COPY package.json bun.lock ./
COPY patches ./patches
COPY packages/app/package.json ./packages/app/package.json
COPY packages/server/package.json ./packages/server/package.json
# --ignore-scripts skips the workspace postinstall, which is `prisma generate`
# and needs a schema this layer deliberately does not carry. Bun never runs
# dependency install scripts at all, so nothing else is lost by it.
RUN --mount=type=cache,target=/root/.bun/install/cache \
    bun install --frozen-lockfile --ignore-scripts

# The generated Prisma client is shared: the app imports the server's router for
# its types, so `next build` cannot typecheck until this exists.
FROM deps AS source
COPY . .
RUN bun run --cwd packages/server generate

FROM source AS server
ENV NODE_ENV=production
EXPOSE 3000
# exec so Bun is PID 1 and receives SIGTERM directly. Migrations run first, so a
# deploy is never serving against a schema it has not applied yet.
CMD ["sh", "-c", "cd packages/server && exec bun run start"]

FROM source AS app
# Next inlines this into the client bundle, so it has to be present at build
# time. Changing it later is a rebuild, not a restart.
ARG NEXT_PUBLIC_SERVER_URL
ENV NEXT_PUBLIC_SERVER_URL=$NEXT_PUBLIC_SERVER_URL
RUN bun run --cwd packages/app build
ENV NODE_ENV=production
EXPOSE 3000
CMD ["sh", "-c", "cd packages/app && exec bun run start"]
