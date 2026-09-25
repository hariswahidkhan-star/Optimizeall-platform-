#!/usr/bin/env bash
# Manages the EF Core migrations of BOTH database providers:
#   MySQL   backend/src/OptimizeAll.Infrastructure/Persistence/Migrations
#   SQLite  backend/src/OptimizeAll.Infrastructure.Sqlite/Migrations
# and verifies that neither provider has pending model changes.
#
# Usage:
#   scripts/regenerate-migrations.sh --add NAME   # after a model change: add migration NAME to both providers
#   scripts/regenerate-migrations.sh --check      # only run has-pending-model-changes for both providers
#   scripts/regenerate-migrations.sh              # same as --check: it never deletes or regenerates migrations
#
# The InitialCreate migrations are FROZEN baselines (docs/DATABASE.md § Migrations): every deployed database records
# their ids in __EFMigrationsHistory, so they are never deleted or regenerated (a regenerated baseline gets a new id,
# and every existing database would then fail on startup). MigrationBaselineTests pins their ids. Only a repository
# without any migrations gets an InitialCreate (created by --add before the named migration).
#
# Needs no database server: design time uses the API host (src/OptimizeAll.Api) with Database:Provider switched per
# run; MySQL uses a fixed server version, SQLite a throwaway file in a temp directory.
# Requires dotnet-ef 8.0.x (dotnet tool install --global dotnet-ef --version 8.0.10).
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

mode=check; name=""
while [ $# -gt 0 ]; do
  case "$1" in
    --add) mode=add; name="${2:?--add needs a migration name}"; shift ;;
    --check) mode=check ;;
    -h|--help) sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "Unknown option: $1 (see --help)" ;;
  esac
  shift
done

if [ "$mode" = add ]; then
  case "$name" in
    *InitialCreate*) die "InitialCreate is the frozen baseline; name the migration after the change (e.g. AddCampaignBudget)" ;;
    *[!A-Za-z0-9_]*) die "Migration names are C# identifiers (letters, digits, underscores): $name" ;;
  esac
fi

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

has_baseline() { compgen -G "$1/*_InitialCreate.cs" >/dev/null; }

if [ "$mode" = add ]; then
  if ! has_baseline "$mysql_project/$mysql_dir" && ! has_baseline "$sqlite_project/$sqlite_dir"; then
    log "No migrations yet: creating the InitialCreate baselines"
    ef_for MySql migrations add InitialCreate -p "$mysql_project" -s "$startup" -o "$mysql_dir"
    ef_for Sqlite migrations add InitialCreate -p "$sqlite_project" -s "$startup" -o "$sqlite_dir"
    dotnet build "$startup" -nologo -v q >/dev/null
  fi
  { has_baseline "$mysql_project/$mysql_dir" && has_baseline "$sqlite_project/$sqlite_dir"; } \
    || die "Only one provider has its InitialCreate baseline; restore the missing one from git (never regenerate it)"
  log "MySQL: migrations add $name"
  ef_for MySql migrations add "$name" -p "$mysql_project" -s "$startup" -o "$mysql_dir"
  log "SQLite: migrations add $name"
  ef_for Sqlite migrations add "$name" -p "$sqlite_project" -s "$startup" -o "$sqlite_dir"
  dotnet build "$startup" -nologo -v q >/dev/null
fi

pending() { die "$1 has model changes without a migration: run scripts/regenerate-migrations.sh --add <ChangeName>"; }
log "MySQL: has-pending-model-changes"
ef_for MySql migrations has-pending-model-changes -p "$mysql_project" -s "$startup" || pending MySQL
log "SQLite: has-pending-model-changes"
ef_for Sqlite migrations has-pending-model-changes -p "$sqlite_project" -s "$startup" || pending SQLite
ok "Migrations are up to date for MySQL and SQLite"
