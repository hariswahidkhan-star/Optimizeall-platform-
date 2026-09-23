#!/usr/bin/env bash
# Stops processes started by scripts/dev-start.sh (by PID file in .dev/; the whole process group is
# terminated so `dotnet run`/`npm` children don't linger).
#
# Usage:
#   scripts/dev-stop.sh [api|web ...]     # default: all
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

case "${1:-}" in -h|--help) sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;; esac

names=("$@")
[ ${#names[@]} -gt 0 ] || names=(web api)

for name in "${names[@]}"; do
  pid_file="$DEV_DIR/$name.pid"
  if [ ! -f "$pid_file" ]; then
    log "$name: not running (no PID file)"
    continue
  fi
  pid="$(cat "$pid_file")"
  if [[ "$pid" =~ ^[0-9]+$ ]] && kill -0 "$pid" 2>/dev/null; then
    log "Stopping $name (pid $pid)"
    stop_bg "$pid"
    ok "$name stopped"
  else
    log "$name: process $pid already exited"
  fi
  rm -f "$pid_file"
done
