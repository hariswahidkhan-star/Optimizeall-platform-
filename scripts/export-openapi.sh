#!/usr/bin/env bash
# Exports the API's OpenAPI document to docs/api/openapi.json.
#
# Builds the API, starts it briefly with Swagger enabled against a throwaway database (created by the
# startup migration, dropped afterwards), downloads /api/docs/v1/openapi.json and stops it.
#
# Usage:
#   scripts/export-openapi.sh [--output PATH] [--port PORT] [--no-db]
#
#   --output PATH  default docs/api/openapi.json
#   --port PORT    default 5099
#   --no-db        don't create a database; start with Database:InitializeOnStartup=false
#                  (faster; works while no module touches the database during startup)
#
# Environment: DB_HOST DB_PORT DB_USER DB_PASSWORD (see scripts/dev-setup.sh). The user needs rights to
# create/drop `optimizeall_%` databases (granted by dev-setup.sh).
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

output="$ROOT/docs/api/openapi.json"
port=5099
use_db=true
while [ $# -gt 0 ]; do
  case "$1" in
    --output) output="$2"; shift ;;
    --port) port="$2"; shift ;;
    --no-db) use_db=false ;;
    -h|--help) sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "Unknown option: $1 (see --help)" ;;
  esac
  shift
done

have dotnet || die "dotnet is required"
have curl || die "curl is required"
port_in_use "$port" && die "Port $port is in use; pass --port"

tmp="$(mktemp -d)"
db_name="optimizeall_openapi_$(date +%s)_$$"
api_pid=""

cleanup() {
  local status=$?
  [ -n "$api_pid" ] && stop_bg "$api_pid"
  if $use_db; then
    mysql_app -e "DROP DATABASE IF EXISTS \`$db_name\`" 2>/dev/null \
      || warn "Could not drop temporary database $db_name (mysql client missing?); drop it manually."
  fi
  if [ "$status" -ne 0 ] && [ -f "$tmp/api.log" ]; then
    echo "---- API log (tail) ----" >&2
    tail -n 40 "$tmp/api.log" >&2 || true
  fi
  rm -rf "$tmp"
  exit "$status"
}
trap cleanup EXIT

log "Building API (Release)"
dotnet build "$API_PROJECT/OptimizeAll.Api.csproj" -c Release -nologo -v quiet >/dev/null
bin_dir="$API_PROJECT/bin/Release/net8.0"
[ -f "$bin_dir/OptimizeAll.Api.dll" ] || die "Build output not found in $bin_dir"

log "Starting API on :$port with Swagger enabled ($($use_db && echo "temporary database $db_name" || echo "no database"))"
# Environment "OpenApiExport" is non-Production (file email allowed) and loads no appsettings.<env>.json.
api_pid="$(cd "$bin_dir" && \
  ASPNETCORE_ENVIRONMENT=OpenApiExport \
  ASPNETCORE_URLS="http://127.0.0.1:$port" \
  ConnectionStrings__Default="$(connection_string "$db_name")" \
  Database__InitializeOnStartup="$use_db" \
  Database__InitializationMode=Migrate \
  Database__Seed__0=Baseline \
  Jwt__SigningKey="openapi-export-only-signing-key-0123456789abcdef" \
  Security__HashSalt="openapi-export-only" \
  Security__SecureCookies=false \
  Email__Mode=File \
  Email__PickupDirectory="$tmp/mail" \
  Storage__RootPath="$tmp/files" \
  Jobs__Enabled=false \
  RateLimiting__Enabled=false \
  Swagger__Enabled=true \
  Bootstrap__AdminEmail='' \
  Bootstrap__AdminPassword='' \
  start_bg api "$tmp/api.log" dotnet OptimizeAll.Api.dll)"

url="http://127.0.0.1:$port/api/docs/v1/openapi.json"
wait_for_url "$url" 180 "$api_pid" || die "API did not serve $url"

mkdir -p "$(dirname "$output")"
curl -fsS "$url" -o "$tmp/openapi.raw.json"
if have node; then
  node -e 'const fs=require("fs");const [i,o]=process.argv.slice(1);fs.writeFileSync(o,JSON.stringify(JSON.parse(fs.readFileSync(i,"utf8")),null,2)+"\n")' \
    "$tmp/openapi.raw.json" "$output"
elif have python3; then
  python3 -c 'import json,sys; json.dump(json.load(open(sys.argv[1])), open(sys.argv[2],"w"), indent=2); open(sys.argv[2],"a").write("\n")' \
    "$tmp/openapi.raw.json" "$output"
else
  cp "$tmp/openapi.raw.json" "$output"
fi

paths="$(grep -c '^    "/' "$output" || true)"
ok "Wrote ${output#"$ROOT/"} ($paths paths)"
