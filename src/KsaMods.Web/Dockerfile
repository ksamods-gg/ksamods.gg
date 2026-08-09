# The frontend. Build context is the repository root.
#
# Two build stages because Tailwind needs Node and the app needs the .NET SDK, and putting Node
# into the SDK image would rebuild npm packages on every C# change.

FROM node:22-alpine AS css
WORKDIR /css

# package.json alone first, so `npm ci` is cached until dependencies actually change.
COPY src/KsaMods.Web/package.json src/KsaMods.Web/package-lock.json* ./
RUN npm install --no-audit --no-fund

COPY src/KsaMods.Web/Styles/ Styles/
# Tailwind v4 scans sources for the utilities it emits, so the markup has to be present or the
# stylesheet comes out holding nothing but the reset.
COPY src/KsaMods.Web/Components/ Components/
RUN npx tailwindcss -i ./Styles/app.css -o ./app.css --minify

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props BannedSymbols.txt ./
COPY src/KsaMods.Metadata/ src/KsaMods.Metadata/
COPY src/KsaMods.Web/ src/KsaMods.Web/

# The stylesheet is built above; skip the MSBuild target that shells out to npm, which is not
# installed in this stage.
COPY --from=css /css/app.css src/KsaMods.Web/wwwroot/app.css

RUN dotnet publish src/KsaMods.Web/KsaMods.Web.csproj \
      -c Release -o /app --no-self-contained -p:SkipTailwind=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime

USER 64198:64198
WORKDIR /app
COPY --from=build --chown=root:root --chmod=555 /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080
ENTRYPOINT ["/app/KsaMods.Web"]
