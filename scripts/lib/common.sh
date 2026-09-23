#!/usr/bin/env bash
# Shared helpers for scripts/*.sh. Source it; do not execute.
# shellcheck disable=SC2034

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BACKEND_DIR="$ROOT/backend"
FRONTEND_DIR="${FRONTEND_DIR:-$ROOT/frontend}"
API_PROJECT="$BACKEND_DIR/src/OptimizeAll.Api"
DEV_DIR="$ROOT/.dev"

# Local database defaults (match backend/src/OptimizeAll.Api/appsettings.Development.json).
DB_HOST="${DB_HOST:-127.0.0.1}"
DB_PORT="${DB_PORT:-3306}"
DB_NAME="${DB_NAME:-optimizeall_dev}"
DB_USER="${DB_USER:-optimizeall}"
DB_PASSWORD="${DB_PASSWORD:-optimizeall_dev}"

if [ -t 1 ]; then
  _c_info=$'\033[1;34m'; _c_ok=$'\033[1;32m'; _c_warn=$'\033[1;33m'; _c_err=$'\033[1;31m'; _c_off=$'\033[0m'
else
  _c_info=""; _c_ok=""; _c_warn=""; _c_err=""; _c_off=""
fi

log()  { printf '%s==>%s %s\n' "$_c_info" "$_c_off" "$*"; }
ok()   { printf '%s ok%s %s\n' "$_c_ok" "$_c_off" "$*"; }
warn() { printf '%swarn%s %s\n' "$_c_warn" "$_c_off" "$*" >&2; }
die()  { printf '%serror%s %s\n' "$_c_err" "$_c_off" "$*" >&2; exit 1; }

have() { command -v "$1" >/dev/null 2>&1; }

# Server-level connection string (no database), as used by the integration tests.
server_connection_string() {
  printf 'Server=%s;Port=%s;User=%s;Password=%s;' "$DB_HOST" "$DB_PORT" "$DB_USER" "$DB_PASSWORD"
}

# Connection string for database $1 (default $DB_NAME).
connection_string() {
  printf 'Server=%s;Port=%s;Database=%s;User=%s;Password=%s;' "$DB_HOST" "$DB_PORT" "${1:-$DB_NAME}" "$DB_USER" "$DB_PASSWORD"
}

# Runs SQL as the application user over TCP (password via MYSQL_PWD, never on the command line).
mysql_app() {
  have mysql || return 127
  MYSQL_PWD="$DB_PASSWORD" mysql --protocol=TCP -h "$DB_HOST" -P "$DB_PORT" -u "$DB_USER" -N -B "$@"
}

# True if something accepts TCP connections on 127.0.0.1:$1.
port_in_use() {
  (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null
}

# wait_for_url URL TIMEOUT_SECONDS [PID] — polls until HTTP 2xx; fails early if PID exits.
wait_for_url() {
  local url="$1" timeout="$2" pid="${3:-}" waited=0
  until curl -fsS -o /dev/null --max-time 5 "$url" 2>/dev/null; do
    if [ -n "$pid" ] && ! kill -0 "$pid" 2>/dev/null; then
      return 1
    fi
    if [ "$waited" -ge "$timeout" ]; then
      return 1
    fi
    sleep 2
    waited=$((waited + 2))
  done
}

# start_bg NAME LOGFILE CMD... — starts CMD in the background in its own session/process group when
# `setsid` is available, so stop_bg can terminate the whole tree (dotnet run / npm spawn children).
# Prints the PID.
start_bg() {
  local log_file="$2"; shift 2
  if have setsid; then
    setsid "$@" >"$log_file" 2>&1 < /dev/null &
  else
    "$@" >"$log_file" 2>&1 < /dev/null &
  fi
  echo $!
}

# stop_bg PID — TERM the process group (or the process and its children), then KILL after 15s.
stop_bg() {
  local pid="$1" i
  kill -0 "$pid" 2>/dev/null || return 0
  local pgid
  pgid="$(ps -o pgid= -p "$pid" 2>/dev/null | tr -d ' ' || true)"
  if [ -n "$pgid" ] && [ "$pgid" = "$pid" ]; then
    kill -TERM -- "-$pid" 2>/dev/null || true
  else
    have pkill && pkill -TERM -P "$pid" 2>/dev/null || true
    kill -TERM "$pid" 2>/dev/null || true
  fi
  for i in $(seq 1 15); do
    kill -0 "$pid" 2>/dev/null || return 0
    sleep 1
  done
  if [ -n "$pgid" ] && [ "$pgid" = "$pid" ]; then
    kill -KILL -- "-$pid" 2>/dev/null || true
  else
    kill -KILL "$pid" 2>/dev/null || true
  fi
}

ensure_dev_dir() {
  mkdir -p "$DEV_DIR"
  # Keep runtime files (PIDs, logs) out of git without touching the repo .gitignore.
  [ -f "$DEV_DIR/.gitignore" ] || printf '*\n' > "$DEV_DIR/.gitignore"
}
