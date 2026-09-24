#!/usr/bin/env bash
# Runs the full-stack Playwright journeys (frontend/e2e/journeys) against a real API and a fresh MySQL database.
#
#   1. creates a fresh database oa_e2e_<timestamp> (MySQL), or a fresh SQLite file with E2E_DB_PROVIDER=sqlite
#   2. builds and starts the API on :$E2E_API_PORT (Development, $E2E_SEED seed profiles, file-mode email + dev mailbox,
#      background jobs off, relaxed auth rate limit, bootstrap admin, non-production test sign-in on, Google sign-in
#      not configured)
#   3. builds the frontend and serves it with `vite preview` on :$E2E_WEB_PORT, proxying /api/, /t/ and /e/ to the API
#   4. waits for /health/ready and runs `E2E_SUITE=$E2E_SUITE npx playwright test` (desktop + mobile projects)
#   5. always tears down (kills the servers by PID, drops the database) and exits with Playwright's exit code
#
# Usage:
#   scripts/e2e-journeys.sh [extra playwright args...]      e.g. --project=desktop-chromium, --repeat-each=1
#
# Environment:
#   E2E_SUITE=journeys    Playwright suite (frontend/e2e/<suite>): "journeys" (participant → admin, Baseline seed),
#                         "agency" (agency platform journeys against the Demo seed's accounts and clients),
#                         "platform" (test users, login-as, custom roles, payments hub, editing, Google sign-in off;
#                         Demo seed), "a11y" (accessibility & responsive audit of every portal; Demo seed,
#                         read-only), "crawl" (every role walks every page and the public website; Demo seed,
#                         read-only), or one of the j-* journeys (each walks one role's work end to end, with its
#                         negatives; see the header of frontend/playwright.config.ts):
#                           j-participant   participant lifecycle, registration to payout (Baseline seed)
#                           j-delivery      client delivery through the client portal (Demo seed)
#                           j-campaigns     campaign manager + reviewer (Demo seed)
#                           j-finance       payouts and the payments hub (Demo seed)
#                           j-admin         platform administration: users, roles, test users, log-in-as (Demo seed)
#                           j-lead-to-cash  website inquiry → CRM → proposal → contract → paid recurring invoice (Demo seed)
#   E2E_SEED              comma-separated seed profiles (default: Baseline for journeys and j-participant,
#                         Baseline,Demo for everything else)
#   E2E_DB_PROVIDER=mysql mysql (default) or sqlite (a fresh file in $E2E_WORK_DIR; no MySQL server needed)
#   DB_HOST/DB_PORT/DB_USER/DB_PASSWORD   MySQL server (defaults: 127.0.0.1:3306 optimizeall/optimizeall_dev);
#                                         the user must be able to CREATE/DROP databases
#   E2E_API_PORT=5099  E2E_WEB_PORT=4173
#   E2E_SKIP_BUILD=1   reuse existing API (Release) and frontend (dist/) builds (CI builds them in earlier steps)
#   E2E_KEEP_DB=1      keep the database after the run (for debugging)
#   E2E_WORK_DIR       where mail, uploaded files and server logs go (default: a new mktemp directory)
set -uo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

E2E_API_PORT="${E2E_API_PORT:-5099}"
E2E_WEB_PORT="${E2E_WEB_PORT:-4173}"
E2E_DB_NAME="${E2E_DB_NAME:-oa_e2e_$(date +%Y%m%d%H%M%S)_$$}"
E2E_WORK_DIR="${E2E_WORK_DIR:-$(mktemp -d "${TMPDIR:-/tmp}/oa-e2e.XXXXXX")}"
MAIL_DIR="$E2E_WORK_DIR/mail"
FILES_DIR="$E2E_WORK_DIR/files"
API_LOG="$E2E_WORK_DIR/api.log"
WEB_LOG="$E2E_WORK_DIR/web.log"
ADMIN_EMAIL="${E2E_ADMIN_EMAIL:-e2e-admin@optimizeall.test}"
ADMIN_PASSWORD="${E2E_ADMIN_PASSWORD:-E2e-Admin#Journey-2026}"
E2E_SUITE="${E2E_SUITE:-journeys}"
case "$E2E_SUITE" in
  journeys|j-participant) E2E_SEED="${E2E_SEED:-Baseline}" ;;
  agency|platform|a11y|crawl|j-*) E2E_SEED="${E2E_SEED:-Baseline,Demo}" ;;
  *) E2E_SEED="${E2E_SEED:-Baseline}" ;;
esac
E2E_DB_PROVIDER="$(printf '%s' "${E2E_DB_PROVIDER:-mysql}" | tr '[:upper:]' '[:lower:]')"
case "$E2E_DB_PROVIDER" in mysql|sqlite) ;; *) die "E2E_DB_PROVIDER must be mysql or sqlite" ;; esac
SQLITE_FILE="$E2E_WORK_DIR/e2e.db"

if [ "$E2E_DB_PROVIDER" = mysql ]; then
  have mysql || die "The mysql client is required (it creates and drops the e2e database)"
fi
have dotnet || die "dotnet is required"
have npx || die "Node.js/npm is required"

free_port() { ! (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null; }
free_port "$E2E_API_PORT" || die "Port $E2E_API_PORT is in use"
free_port "$E2E_WEB_PORT" || die "Port $E2E_WEB_PORT is in use"

mkdir -p "$MAIL_DIR" "$FILES_DIR"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

api_pid=""; web_pid=""; db_created=false
teardown() {
  local code=$?
  [ -n "$web_pid" ] && stop_bg "$web_pid"
  [ -n "$api_pid" ] && stop_bg "$api_pid"
  if $db_created && [ "$E2E_DB_PROVIDER" = sqlite ]; then
    [ "${E2E_KEEP_DB:-}" = "1" ] && log "Database kept: $SQLITE_FILE" || rm -f "$SQLITE_FILE" "$SQLITE_FILE-wal" "$SQLITE_FILE-shm"
  elif $db_created && [ "${E2E_KEEP_DB:-}" != "1" ]; then
    local i
    for i in 1 2 3 4 5 6; do
      mysql_app -e "DROP DATABASE IF EXISTS \`$E2E_DB_NAME\`" && break
      [ "$i" = 6 ] && warn "Could not drop $E2E_DB_NAME" || sleep 5
    done
  fi
  log "Logs: $API_LOG, $WEB_LOG"
  exit "$code"
}
trap teardown EXIT
trap 'exit 130' INT TERM

# ------------------------------------------------------------------ database
if [ "$E2E_DB_PROVIDER" = sqlite ]; then
  log "Using a fresh SQLite database $SQLITE_FILE"
  rm -f "$SQLITE_FILE" "$SQLITE_FILE-wal" "$SQLITE_FILE-shm"
  db_created=true
  db_env=(Database__Provider=Sqlite Database__SqlitePath="$SQLITE_FILE")
else
  log "Creating database $E2E_DB_NAME"
  # Retries for a while: a shared MySQL server may briefly refuse connections (ERROR 1040 Too many connections).
  for attempt in $(seq 1 30); do
    if mysql_app -e "CREATE DATABASE \`$E2E_DB_NAME\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci"; then
      db_created=true; break
    fi
    warn "Could not create $E2E_DB_NAME (attempt $attempt); retrying"
    sleep 5
  done
  $db_created || die "Could not create $E2E_DB_NAME"
  db_env=(ConnectionStrings__Default="$(connection_string "$E2E_DB_NAME")")
fi
seed_env=()
IFS=',' read -r -a seed_profiles <<< "$E2E_SEED"
for i in "${!seed_profiles[@]}"; do seed_env+=("Database__Seed__$i=${seed_profiles[$i]// /}"); done
# Clears the next index so appsettings.Development.json's list ("Baseline", "Demo") cannot leak into a shorter one.
seed_env+=("Database__Seed__${#seed_profiles[@]}=None")

# ------------------------------------------------------------------ builds
if [ "${E2E_SKIP_BUILD:-}" != "1" ]; then
  log "Building API (Release)"
  dotnet build "$API_PROJECT" -c Release -nologo -v quiet || die "API build failed"
  if [ ! -d "$FRONTEND_DIR/node_modules" ]; then
    log "Installing frontend dependencies"
    (cd "$FRONTEND_DIR" && npm ci --no-audit --no-fund) || die "npm ci failed"
  fi
  log "Building frontend"
  (cd "$FRONTEND_DIR" && npm run build) || die "Frontend build failed"
fi

# ------------------------------------------------------------------ API
log "Starting API on :$E2E_API_PORT ($E2E_DB_PROVIDER, seed: $E2E_SEED)"
api_pid="$(cd "$ROOT" && start_bg e2e-api "$API_LOG" env \
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://localhost:$E2E_API_PORT" \
  "${db_env[@]}" \
  Database__InitializationMode=Migrate \
  "${seed_env[@]}" \
  Email__Mode=File Email__PickupDirectory="$MAIL_DIR" Email__AppBaseUrl="http://localhost:$E2E_WEB_PORT" \
  DevTools__MailboxEnabled=true DevTools__TestLoginEnabled=true \
  Authentication__Google__ClientId= Authentication__Google__ClientSecret= \
  RateLimiting__AuthPerMinute=1000 \
  Jobs__Enabled=false \
  Storage__RootPath="$FILES_DIR" \
  Bootstrap__AdminEmail="$ADMIN_EMAIL" Bootstrap__AdminPassword="$ADMIN_PASSWORD" \
  dotnet run --project "$API_PROJECT" -c Release --no-build --no-launch-profile)"

# ------------------------------------------------------------------ web
log "Starting vite preview on :$E2E_WEB_PORT (proxy → :$E2E_API_PORT)"
web_pid="$(cd "$FRONTEND_DIR" && VITE_API_PROXY_TARGET="http://127.0.0.1:$E2E_API_PORT" \
  start_bg e2e-web "$WEB_LOG" npx vite preview --port "$E2E_WEB_PORT" --strictPort --host localhost)"

wait_for_url "http://localhost:$E2E_API_PORT/health/ready" 300 "$api_pid" \
  || { tail -n 60 "$API_LOG" >&2; die "API did not become ready"; }
ok "API ready"
wait_for_url "http://localhost:$E2E_WEB_PORT/" 60 "$web_pid" \
  || { tail -n 40 "$WEB_LOG" >&2; die "vite preview did not start"; }
ok "Web ready"

# ------------------------------------------------------------------ playwright
log "Running Playwright suite $E2E_SUITE"
set +e
(cd "$FRONTEND_DIR" && \
  E2E_SUITE="$E2E_SUITE" E2E_DB_PROVIDER="$E2E_DB_PROVIDER" \
  PLAYWRIGHT_BASE_URL="http://localhost:$E2E_WEB_PORT" E2E_BASE_URL="http://localhost:$E2E_WEB_PORT" \
  E2E_API_URL="http://localhost:$E2E_API_PORT" E2E_MAIL_DIR="$MAIL_DIR" \
  E2E_ADMIN_EMAIL="$ADMIN_EMAIL" E2E_ADMIN_PASSWORD="$ADMIN_PASSWORD" \
  E2E_DB_NAME="$E2E_DB_NAME" E2E_DB_HOST="$DB_HOST" E2E_DB_PORT="$DB_PORT" \
  E2E_DB_USER="$DB_USER" E2E_DB_PASSWORD="$DB_PASSWORD" \
  npx playwright test "$@")
pw_code=$?
if [ "$pw_code" -eq 0 ]; then ok "Journeys passed"; else warn "Journeys failed (exit $pw_code)"; fi
exit "$pw_code"
