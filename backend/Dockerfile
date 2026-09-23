# syntax=docker/dockerfile:1.7
#
# Optimize All API image.
#
# Build context: backend/
#   docker build -t optimizeall-api backend                       # API runtime image (default target)
#   docker build -t optimizeall-migrator --target migrator backend  # one-shot EF migration bundle
#
# Targets
#   build     restore (cached layer) + publish
#   efbundle  produces /out/efbundle (self-contained EF Core migrations bundle, MySQL)
#   migrator  small image that runs the bundle:  docker run --rm -e ConnectionStrings__Default="<conn>" optimizeall-migrator
#   runtime   (default) non-root ASP.NET Core runtime listening on :8080
#
# Database: MySQL 8 (default) or SQLite (Database__Provider=Sqlite, Database__SqlitePath=/app/storage/db/optimizeall.db).
# With SQLite the database file lives on the /app/storage volume next to the uploaded files; the API applies the
# SQLite migrations on startup (Database__InitializationMode=Migrate), so the migrator image is only for MySQL.
#
# Alpine variants are used because the app runs with InvariantGlobalization (no ICU needed) and busybox
# `wget` gives us a HEALTHCHECK without installing curl.

ARG DOTNET_VERSION=8.0

# ---------------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-alpine AS build
WORKDIR /src

# Restore layer: only project files + shared build props, so source edits don't invalidate the NuGet cache.
COPY Directory.Build.props ./
COPY src/OptimizeAll.Domain/OptimizeAll.Domain.csproj                 src/OptimizeAll.Domain/
COPY src/OptimizeAll.Infrastructure/OptimizeAll.Infrastructure.csproj src/OptimizeAll.Infrastructure/
COPY src/OptimizeAll.Infrastructure.Sqlite/OptimizeAll.Infrastructure.Sqlite.csproj src/OptimizeAll.Infrastructure.Sqlite/
COPY src/OptimizeAll.Api/OptimizeAll.Api.csproj                       src/OptimizeAll.Api/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/OptimizeAll.Api/OptimizeAll.Api.csproj

COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/OptimizeAll.Api/OptimizeAll.Api.csproj \
        -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---------------------------------------------------------------------------------------------------------
FROM build AS efbundle
ARG DOTNET_EF_VERSION=8.0.10
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --version ${DOTNET_EF_VERSION}
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet ef migrations bundle \
        --project src/OptimizeAll.Infrastructure/OptimizeAll.Infrastructure.csproj \
        --startup-project src/OptimizeAll.Api/OptimizeAll.Api.csproj \
        --configuration Release \
        --self-contained \
        --output /out/efbundle \
        --verbose

# ---------------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION}-alpine AS migrator
WORKDIR /app
COPY --from=efbundle --chown=root:root --chmod=0755 /out/efbundle /app/efbundle
# The single-file bundle extracts native libraries at start; /tmp keeps it working with a read-only root filesystem.
ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp
USER $APP_UID
# The bundle boots the API's host to build the DbContext, so ConnectionStrings__Default must be set
# (the bundle ships without appsettings.json). --connection "<conn>" may additionally override it.
# Usage:  docker run --rm -e ConnectionStrings__Default="Server=...;Database=...;User=...;Password=...;" optimizeall-migrator
#         docker run --rm -e ConnectionStrings__Default="..." optimizeall-migrator <TargetMigration>   # roll back/forward to a target
ENTRYPOINT ["/app/efbundle"]

# ---------------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-alpine AS runtime
WORKDIR /app

# Writable state lives under /app/storage (uploaded screenshots/files; dev email pickup; with SQLite the database in
# db/). Data-protection keys are stored in the database, so nothing else needs to be persisted.
RUN mkdir -p /app/storage/files /app/storage/mail /app/storage/db && chown -R $APP_UID:$APP_UID /app/storage

# Entrypoint: supports Docker/Kubernetes secret files. For any env var NAME_FILE=/path, exports NAME=<file contents>
# (e.g. Jwt__SigningKey_FILE=/run/secrets/jwt_signing_key), then starts the API as the non-root app user.
# When started as root (platforms that mount a fresh root-owned disk at /app/storage and cannot choose the user,
# e.g. Render), it first gives the app user ownership of the storage directories, then drops privileges with
# BusyBox su. When started as the app user (`user: 1654` in compose/Kubernetes), that step is skipped.
COPY --chmod=0755 <<'EOF' /usr/local/bin/docker-entrypoint.sh
#!/bin/sh
set -eu
for var in $(env | sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)_FILE=.*/\1/p'); do
  file="$(printenv "${var}_FILE")"
  if [ ! -r "$file" ]; then
    echo "docker-entrypoint: ${var}_FILE points to unreadable file '$file'" >&2
    exit 1
  fi
  export "$var=$(cat "$file")"
  unset "${var}_FILE"
done
if [ "$(id -u)" = "0" ]; then
  mkdir -p /app/storage/files /app/storage/mail /app/storage/db
  if [ "$(stat -c %u /app/storage)" != "$APP_UID" ] || [ "$(stat -c %u /app/storage/files)" != "$APP_UID" ] \
     || [ "$(stat -c %u /app/storage/db)" != "$APP_UID" ]; then
    chown -R "$APP_UID:$APP_UID" /app/storage
  fi
  # BusyBox su (no extra package): -m keeps the environment, the app user is uid $APP_UID ("app").
  exec su -m -s /bin/sh app -c 'exec dotnet /app/OptimizeAll.Api.dll "$@"' dotnet "$@"
fi
exec dotnet /app/OptimizeAll.Api.dll "$@"
EOF

COPY --from=build --chown=root:root /app/publish ./

# ASPNETCORE_HTTP_PORTS (set by the base image) is cleared so ASPNETCORE_URLS is the single source of truth.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    Storage__RootPath=/app/storage/files \
    Email__PickupDirectory=/app/storage/mail

VOLUME ["/app/storage"]
EXPOSE 8080
# No USER here: the entrypoint drops to $APP_UID itself (see above). Orchestrators should still set the user.

# Liveness only (no DB dependency). Orchestrators should use /health/ready for readiness/traffic.
HEALTHCHECK --interval=30s --timeout=5s --start-period=90s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
