#!/usr/bin/env bash
# Runs the full local test suite (what CI runs):
#   backend   dotnet build (Release) + unit tests + integration tests against local MySQL
#   frontend  npm ci (if needed), typecheck, lint, unit tests, production build
#   --e2e     additionally starts the API (fresh database, Demo seed) and `vite preview`, then runs Playwright
#
# Usage:
#   scripts/test-all.sh [--e2e] [--skip-backend] [--skip-frontend] [--no-integration]
#
# Environment:
#   OPTIMIZEALL_TEST_MYSQL  server connection string for integration tests
#                           (default built from DB_HOST/DB_PORT/DB_USER/DB_PASSWORD, see scripts/dev-setup.sh)
#   E2E_API_PORT=5080       API port for e2e (must match the Vite proxy target)
#   E2E_WEB_PORT=4173       `vite preview` port
#   E2E_DB_NAME=optimizeall_e2e  recreated on every --e2e run
#
# Test results (TRX) are written to TestResults/ at the repo root.
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

run_backend=true; run_frontend=true; run_integration=true; run_e2e=false
for arg in "$@"; do
  case "$arg" in
    --e2e) run_e2e=true ;;
    --skip-backend) run_backend=false ;;
    --skip-frontend) run_frontend=false ;;
    --no-integration) run_integration=false ;;
    -h|--help) sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "Unknown option: $arg (see --help)" ;;
  esac
done

export CI="${CI:-true}"   # non-interactive runners (vitest runs once, Playwright doesn't open reports)
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
results_dir="$ROOT/TestResults"
has_frontend=false
[ -f "$FRONTEND_DIR/package.json" ] && has_frontend=true
failed=()

# step LABEL CMD... — runs CMD, records failures and keeps going so one run reports everything.
step() {
  local label="$1"; shift
  log "$label"
  if "$@"; then ok "$label"; else failed+=("$label"); warn "FAILED: $label"; fi
}

# ------------------------------------------------------------------ backend
if $run_backend; then
  have dotnet || die "dotnet is required"
  log "Backend: build"
  dotnet build "$BACKEND_DIR/OptimizeAll.sln" -c Release -nologo || die "Backend build failed"

  step "Backend: unit tests" \
    dotnet test "$BACKEND_DIR/tests/OptimizeAll.UnitTests" -c Release --no-build -nologo \
      --logger "trx;LogFileName=unit.trx" --results-directory "$results_dir"

  if $run_integration; then
    export OPTIMIZEALL_TEST_MYSQL="${OPTIMIZEALL_TEST_MYSQL:-$(server_connection_string)}"
    step "Backend: integration tests (MySQL at $DB_HOST:$DB_PORT)" \
      dotnet test "$BACKEND_DIR/tests/OptimizeAll.IntegrationTests" -c Release --no-build -nologo \
        --logger "trx;LogFileName=integration.trx" --results-directory "$results_dir"
  fi
fi

# ------------------------------------------------------------------ frontend
npm_run() { (cd "$FRONTEND_DIR" && npm run "$@"); }

if $run_frontend; then
  if $has_frontend; then
    have npm || die "npm is required"
    if [ ! -d "$FRONTEND_DIR/node_modules" ]; then
      log "Frontend: npm ci"
      (cd "$FRONTEND_DIR" && npm ci --no-audit --no-fund)
    fi
    step "Frontend: typecheck" npm_run typecheck
    step "Frontend: lint" npm_run lint
    step "Frontend: unit tests" npm_run test
    step "Frontend: build" npm_run build
  else
    warn "frontend/package.json not found; skipping frontend checks"
  fi
fi

# ------------------------------------------------------------------ e2e
if $run_e2e; then
  $has_frontend || die "--e2e needs the frontend"
  [ -d "$FRONTEND_DIR/dist" ] || npm_run build
  E2E_API_PORT="${E2E_API_PORT:-5080}"
  E2E_WEB_PORT="${E2E_WEB_PORT:-4173}"
  E2E_DB_NAME="${E2E_DB_NAME:-optimizeall_e2e}"
  port_in_use "$E2E_API_PORT" && die "Port $E2E_API_PORT is in use (stop the dev stack: scripts/dev-stop.sh)"
  port_in_use "$E2E_WEB_PORT" && die "Port $E2E_WEB_PORT is in use"
  ensure_dev_dir
  e2e_pids=()
  stop_e2e() { local p; for p in ${e2e_pids[@]+"${e2e_pids[@]}"}; do stop_bg "$p"; done; }
  trap stop_e2e EXIT

  log "E2E: recreating database $E2E_DB_NAME"
  mysql_app -e "DROP DATABASE IF EXISTS \`$E2E_DB_NAME\`" \
    || warn "Could not drop $E2E_DB_NAME (mysql client missing?); the API will migrate whatever exists"

  log "E2E: starting API on :$E2E_API_PORT (log: .dev/e2e-api.log)"
  e2e_pids+=("$(cd "$ROOT" && \
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="http://localhost:$E2E_API_PORT" \
    ConnectionStrings__Default="$(connection_string "$E2E_DB_NAME")" \
    Database__Seed__0=Baseline Database__Seed__1=Demo \
    RateLimiting__Enabled=false \
    Email__AppBaseUrl="http://localhost:$E2E_WEB_PORT" \
    Email__PickupDirectory="$DEV_DIR/e2e-mail" \
    Storage__RootPath="$DEV_DIR/e2e-files" \
    start_bg e2e-api "$DEV_DIR/e2e-api.log" dotnet run --project "$API_PROJECT" -c Release --no-launch-profile)")
  wait_for_url "http://localhost:$E2E_API_PORT/health/ready" 300 "${e2e_pids[0]}" \
    || { tail -n 40 "$DEV_DIR/e2e-api.log" >&2; die "API did not start for e2e"; }

  log "E2E: starting vite preview on :$E2E_WEB_PORT (log: .dev/e2e-web.log)"
  e2e_pids+=("$(cd "$FRONTEND_DIR" && start_bg e2e-web "$DEV_DIR/e2e-web.log" \
    npx vite preview --port "$E2E_WEB_PORT" --strictPort)")
  wait_for_url "http://localhost:$E2E_WEB_PORT/" 60 "${e2e_pids[1]}" \
    || { tail -n 40 "$DEV_DIR/e2e-web.log" >&2; die "vite preview did not start"; }

  export PLAYWRIGHT_BASE_URL="http://localhost:$E2E_WEB_PORT" E2E_BASE_URL="http://localhost:$E2E_WEB_PORT"
  export E2E_API_URL="http://localhost:$E2E_API_PORT" E2E_MAIL_DIR="$DEV_DIR/e2e-mail"
  step "E2E: Playwright" npm_run e2e
fi

echo
if [ ${#failed[@]} -gt 0 ]; then
  printf '%s\n' "Failed steps:" "${failed[@]/#/  - }" >&2
  exit 1
fi
ok "All selected checks passed"
