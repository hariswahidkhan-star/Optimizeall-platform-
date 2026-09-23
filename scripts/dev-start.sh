#!/usr/bin/env bash
# Starts the local development stack in the background:
#   API  http://localhost:5080  (ASPNETCORE_ENVIRONMENT=Development, `dotnet run`; migrates + seeds Baseline/Demo)
#   Web  http://localhost:5173  (Vite dev server; proxies /api and /t to the API)
# PIDs go to .dev/*.pid and logs to .dev/*.log. Stop with scripts/dev-stop.sh.
#
# Usage:
#   scripts/dev-start.sh [--api-only | --web-only] [--no-wait]
#
# Environment:
#   API_PORT=5080  WEB_PORT=5173   (the Vite proxy expects the API on 5080)
#   DB_* variables override the connection string (see scripts/dev-setup.sh)
#
# Development conveniences (appsettings.Development.json): Swagger at /api/docs, emails written to
# backend/src/OptimizeAll.Api/storage/mail and shown by the dev mailbox, bootstrap admin
# admin@optimizeall.local / Admin#Demo2026!.
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

API_PORT="${API_PORT:-5080}"
WEB_PORT="${WEB_PORT:-5173}"
start_api=true; start_web=true; wait_ready=true
for arg in "$@"; do
  case "$arg" in
    --api-only) start_web=false ;;
    --web-only) start_api=false ;;
    --no-wait) wait_ready=false ;;
    -h|--help) sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "Unknown option: $arg (see --help)" ;;
  esac
done

have curl || die "curl is required"
ensure_dev_dir

running() { [ -f "$DEV_DIR/$1.pid" ] && kill -0 "$(cat "$DEV_DIR/$1.pid")" 2>/dev/null; }

if $start_api; then
  if running api; then
    ok "API already running (pid $(cat "$DEV_DIR/api.pid"))"
  else
    port_in_use "$API_PORT" && die "Port $API_PORT is already in use (another API instance?)"
    have dotnet || die "dotnet is required"
    log "Starting API on http://localhost:$API_PORT (log: .dev/api.log)"
    pid="$(cd "$ROOT" && ASPNETCORE_ENVIRONMENT=Development \
      ASPNETCORE_URLS="http://localhost:$API_PORT" \
      ConnectionStrings__Default="$(connection_string)" \
      start_bg api "$DEV_DIR/api.log" dotnet run --project "$API_PROJECT" --no-launch-profile)"
    echo "$pid" > "$DEV_DIR/api.pid"
  fi
fi

if $start_web; then
  if running web; then
    ok "Web already running (pid $(cat "$DEV_DIR/web.pid"))"
  elif [ ! -f "$FRONTEND_DIR/package.json" ]; then
    warn "frontend/package.json not found; not starting the web app"
  else
    port_in_use "$WEB_PORT" && die "Port $WEB_PORT is already in use"
    [ -d "$FRONTEND_DIR/node_modules" ] || die "frontend dependencies missing; run scripts/dev-setup.sh"
    log "Starting web on http://localhost:$WEB_PORT (log: .dev/web.log)"
    pid="$(cd "$FRONTEND_DIR" && start_bg web "$DEV_DIR/web.log" npm run dev -- --port "$WEB_PORT" --strictPort)"
    echo "$pid" > "$DEV_DIR/web.pid"
  fi
fi

if $wait_ready; then
  if $start_api && [ -f "$DEV_DIR/api.pid" ]; then
    log "Waiting for API health (first start builds, migrates and seeds; up to 5 min)"
    if wait_for_url "http://localhost:$API_PORT/health/ready" 300 "$(cat "$DEV_DIR/api.pid")"; then
      ok "API ready"
    else
      tail -n 40 "$DEV_DIR/api.log" >&2 || true
      die "API did not become ready; see .dev/api.log"
    fi
  fi
  if $start_web && [ -f "$DEV_DIR/web.pid" ]; then
    if wait_for_url "http://localhost:$WEB_PORT/" 120 "$(cat "$DEV_DIR/web.pid")"; then
      ok "Web ready"
    else
      tail -n 40 "$DEV_DIR/web.log" >&2 || true
      die "Web dev server did not start; see .dev/web.log"
    fi
  fi
fi

cat <<EOF

  Web      http://localhost:$WEB_PORT
  API      http://localhost:$API_PORT   (Swagger: http://localhost:$API_PORT/api/docs)
  Health   http://localhost:$API_PORT/health/ready
  Admin    admin@optimizeall.local / Admin#Demo2026!   (demo accounts: docs/DEMO.md)
  Logs     tail -f .dev/api.log .dev/web.log
  Stop     scripts/dev-stop.sh
EOF
