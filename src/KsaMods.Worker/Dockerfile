# The worker. Build context is the repository root.
#
# This is the one image in the deployment that is NOT chiselled, and the reason is the whole
# design: it shells out to `docker` to run each validation in a throwaway container. That needs a
# docker CLI, which needs a shell, which means a runtime with a package manager.
#
# It also needs the Docker socket, which is root-equivalent on the host. That is why this is a
# separate service from the API: the process reachable from the internet must not be able to reach
# the daemon, and the only way to mean it is for the API's container not to have the socket.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props BannedSymbols.txt ./
COPY src/KsaMods.Metadata/ src/KsaMods.Metadata/
COPY src/KsaMods.Validation/ src/KsaMods.Validation/
COPY src/KsaMods.Resolver/ src/KsaMods.Resolver/
COPY src/KsaMods.Exporter/ src/KsaMods.Exporter/
COPY src/KsaMods.Forge/ src/KsaMods.Forge/
COPY src/KsaMods.Api/ src/KsaMods.Api/
COPY src/KsaMods.Worker/ src/KsaMods.Worker/

RUN dotnet publish src/KsaMods.Worker/KsaMods.Worker.csproj -c Release -o /app --no-self-contained

# aspnet, not runtime. The worker references KsaMods.Api for Database and JobQueue, and that is a
# Web SDK project, so the published output binds Microsoft.AspNetCore.App. The base runtime image
# does not carry that framework and the container exits at startup with "No frameworks were found"
# - which says nothing about the reference that caused it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

# The CLI only. Not the daemon: this container talks to the host's daemon over the mounted socket,
# and running a second one inside would need --privileged, which is a much larger hole than the
# socket already is.
RUN apt-get update \
 && apt-get install -y --no-install-recommends docker.io git ca-certificates \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Root, unlike every other image here, because the socket's group id differs between hosts and a
# worker that cannot reach the daemon does nothing at all. The isolation that matters is around the
# validation container, which has no network, no capabilities and a read-only root - see
# ContainerRunner.
ENV DOTNET_EnableDiagnostics=0 \
    Worker__Scratch=/scratch

# Archives land here and are deleted after each job. Mounted as a tmpfs in compose so a downloaded
# archive never touches a disk that outlives the container - the site does not keep the bytes, and
# the tidiest way to honour that is to make keeping them impossible.
RUN mkdir -p /scratch

ENTRYPOINT ["/app/KsaMods.Worker"]
