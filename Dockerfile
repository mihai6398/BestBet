# syntax=docker/dockerfile:1
#
# One container, one port. The API serves the built SPA as static files, which is what Program.cs
# already assumes with UseDefaultFiles / UseStaticFiles / MapFallbackToFile — so splitting this into
# an nginx container plus an API container would add a moving part without buying anything.

# --- 1. Build the React client ------------------------------------------------
# Debian rather than Alpine on purpose: Vite 8 pulls native rolldown/oxlint binaries, and the
# linux-x64-gnu builds are the well-trodden ones.
FROM node:22-bookworm-slim AS client
WORKDIR /src

# Manifests first, so a source-only edit does not re-run the whole install.
COPY bestbet.client/package.json bestbet.client/package-lock.json ./
RUN npm ci

COPY bestbet.client/ ./
RUN npm run build


# --- 2. Build and publish the API ---------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS server
WORKDIR /src

# ContainerBuild=true drops the SPA proxy package and the esproj project reference: both exist for
# the Visual Studio F5 loop, and the esproj needs a JavaScript SDK this image does not have.
COPY BestBet.Server/BestBet.Server.csproj BestBet.Server/
RUN dotnet restore BestBet.Server/BestBet.Server.csproj -p:ContainerBuild=true

COPY BestBet.Server/ BestBet.Server/
RUN dotnet publish BestBet.Server/BestBet.Server.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        -p:ContainerBuild=true


# --- 3. Runtime ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=server /app/publish ./
COPY --from=client /src/dist ./wwwroot

# The keys that sign the login cookie live on a volume, not in the image layer, so a rebuild signs
# nobody out. Creating the directory with the right owner here is what lets a named volume inherit
# that ownership — without it the non-root user cannot write to the mount. The data itself is in
# PostgreSQL; nothing else in the container needs to be written.
RUN mkdir -p /app/keys && chown -R $APP_UID:$APP_UID /app/keys
USER $APP_UID

# The base image already binds HTTP on 8080 via ASPNETCORE_HTTP_PORTS; setting ASPNETCORE_URLS as
# well only makes Kestrel log a warning about overriding it. Leaving both HTTPS settings unset is
# also what tells Program.cs not to wire up HTTPS redirection. The connection string and every other
# secret come from the environment at run time, never from the image.

EXPOSE 8080
ENTRYPOINT ["dotnet", "BestBet.Server.dll"]
