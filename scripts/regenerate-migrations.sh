#!/usr/bin/env bash
# Regenerates the EF Core migrations for BOTH database providers from the current model:
#   MySQL   backend/src/OptimizeAll.Infrastructure/Persistence/Migrations
#   SQLite  backend/src/OptimizeAll.Infrastructure.Sqlite/Migrations
# then verifies that neither provider has pending model changes.
#
# Usage:
#   scripts/regenerate-migrations.sh              # delete both sets and recreate a single "InitialCreate" (pre-release)
#   scripts/regenerate-migrations.sh --add NAME   # add an incremental migration NAME to both sets (after release)
#   scripts/regenerate-migrations.sh --check      # only run has-pending-model-changes for both providers
#
# Needs no database server: design time uses the API host (src/OptimizeAll.Api) with Database:Provider switched per
# run; MySQL uses a fixed server version, SQLite a throwaway file in a temp directory.
# Requires dotnet-ef 8.0.x (dotnet tool install --global dotnet-ef --version 8.0.10).
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

mode=recreate; name=InitialCreate
while [ $# -gt 0 ]; do
  case "$1" in
    --add) mode=add; name="${2:?--add needs a migration name}"; shift ;;
    --check) mode=check ;;
    -h|--help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "Unknown option: $1 (see --help)" ;;
  esac
  shift
done

if have dotnet-ef; then ef=(dotnet-ef)
elif [ -x "$HOME/.dotnet/tools/dotnet-ef" ]; then ef=("$HOME/.dotnet/tools/dotnet-ef")
elif dotnet ef --version >/dev/null 2>&1; then ef=(dotnet ef)
else die "dotnet-ef not found: dotnet tool install --global dotnet-ef --version 8.0.10"
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 ASPNETCORE_ENVIRONMENT=Development
design_dir="$(mktemp -d)"
trap 'rm -rf "$design_dir"' EXIT

startup=src/OptimizeAll.Api
mysql_project=src/OptimizeAll.Infrastructure
mysql_dir=Persistence/Migrations
sqlite_project=src/OptimizeAll.Infrastructure.Sqlite
sqlite_dir=Migrations

cd "$BACKEND_DIR"
log "Building"
dotnet build "$startup" -nologo -v q >/dev/null || dotnet build "$startup" -nologo

# ef_for PROVIDER ARGS... — runs dotnet-ef with the design-time provider selected through configuration.
ef_for() {
  local provider="$1"; shift
  if [ "$provider" = Sqlite ]; then
    Database__Provider=Sqlite Database__SqlitePath="$design_dir/design.db" "${ef[@]}" "$@" --no-build
  else
    Database__Provider=MySql ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=design;User=design;Password=design;" \
      "${ef[@]}" "$@" --no-build
  fi
}

if [ "$mode" = recreate ]; then
  log "Removing existing migrations"
  rm -f "$mysql_project/$mysql_dir"/*.cs "$sqlite_project/$sqlite_dir"/*.cs
  dotnet build "$startup" -nologo -v q >/dev/null
fi

if [ "$mode" != check ]; then
  log "MySQL: migrations add $name"
  ef_for MySql migrations add "$name" -p "$mysql_project" -s "$startup" -o "$mysql_dir"
  log "SQLite: migrations add $name"
  ef_for Sqlite migrations add "$name" -p "$sqlite_project" -s "$startup" -o "$sqlite_dir"
  dotnet build "$startup" -nologo -v q >/dev/null
fi

log "MySQL: has-pending-model-changes"
ef_for MySql migrations has-pending-model-changes -p "$mysql_project" -s "$startup"
log "SQLite: has-pending-model-changes"
ef_for Sqlite migrations has-pending-model-changes -p "$sqlite_project" -s "$startup"
ok "Migrations are up to date for MySQL and SQLite"
