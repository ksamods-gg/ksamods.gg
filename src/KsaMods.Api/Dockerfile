# The API. Build context is the repository root, because Directory.Build.props and the project
# references live above this directory.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props BannedSymbols.txt ./
COPY src/KsaMods.Metadata/ src/KsaMods.Metadata/
COPY src/KsaMods.Resolver/ src/KsaMods.Resolver/
COPY src/KsaMods.Exporter/ src/KsaMods.Exporter/
COPY src/KsaMods.Forge/ src/KsaMods.Forge/
COPY src/KsaMods.Api/ src/KsaMods.Api/

RUN dotnet publish src/KsaMods.Api/KsaMods.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime

# Chiselled: no shell and no package manager. Nothing to pivot to if the process is compromised,
# and nothing to keep patched that the base image does not already handle.
USER 64198:64198
WORKDIR /app
COPY --from=build --chown=root:root --chmod=555 /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080
ENTRYPOINT ["/app/KsaMods.Api"]
