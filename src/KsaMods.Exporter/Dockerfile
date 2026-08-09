# The exporter. Build context is the repository root.
#
# Not chiselled, because it drives git: GitMirror shells out to the real thing rather than
# reimplementing commit and push. That is deliberate - the mirror's value is that anybody can
# clone it, which means its history has to be an ordinary git history made by ordinary git.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props BannedSymbols.txt ./
COPY src/KsaMods.Metadata/ src/KsaMods.Metadata/
COPY src/KsaMods.Exporter/ src/KsaMods.Exporter/

RUN dotnet publish src/KsaMods.Exporter/KsaMods.Exporter.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS runtime

RUN apt-get update \
 && apt-get install -y --no-install-recommends git ca-certificates \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# The working clone. A volume rather than a fresh clone each run: the mirror grows, and re-cloning
# it every fifteen minutes is bandwidth spent to arrive at the state already on disk.
ENV MIRROR_DIRECTORY=/mirror \
    DOTNET_EnableDiagnostics=0

RUN mkdir -p /mirror

ENTRYPOINT ["/app/KsaMods.Exporter"]
