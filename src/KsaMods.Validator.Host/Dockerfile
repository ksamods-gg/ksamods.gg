# The validation container (backend.md §7.2).
#
# This image handles bytes a stranger produced. Every choice below assumes the parser inside it
# will one day be defeated, and aims to make that worthless: no network, no capabilities, no
# writable filesystem, no shell, no package manager, and a user with no privileges.
#
# The runtime flags matter as much as the image and are NOT expressed here - a Dockerfile cannot
# set them. See docker-run.md next to this file, and KsaMods.Worker/ContainerRunner.cs, which is
# what actually applies them.

# ── build ──────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props BannedSymbols.txt ./
COPY src/KsaMods.Metadata/ src/KsaMods.Metadata/
COPY src/KsaMods.Validation/ src/KsaMods.Validation/
COPY src/KsaMods.Validator.Host/ src/KsaMods.Validator.Host/

RUN dotnet publish src/KsaMods.Validator.Host/KsaMods.Validator.Host.csproj \
      -c Release \
      -o /app \
      --self-contained false \
      -p:PublishTrimmed=false

# ── runtime ────────────────────────────────────────────────────────────────────
# Chiselled: no shell, no package manager, no setuid binaries. If the validator is compromised
# there is nothing in the image to pivot to.
#
# `runtime`, not `runtime-deps`: the publish above is framework-dependent, and runtime-deps
# carries only the native prerequisites for a self-contained build.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS runtime

# 64198 is the non-root `app` user the chiselled images ship. Stated explicitly so a base image
# change that alters the default is a visible diff rather than a silent privilege gain - and it
# must stay in step with the --user flag in KsaMods.Worker/ContainerRunner.cs.
USER 64198:64198

WORKDIR /app
COPY --from=build --chown=root:root --chmod=555 /app /app

# Runtime hardening that belongs to the process rather than the sandbox.
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_TieredPMStubs=0 \
    DOTNET_gcServer=0 \
    KSAMODS_INPUT=/in/archive.zip \
    KSAMODS_OUTPUT=/out/report.json

# Documented, not granted: the worker supplies both as mounts, /in read-only.
VOLUME ["/in", "/out"]

ENTRYPOINT ["/app/KsaMods.Validator.Host"]
