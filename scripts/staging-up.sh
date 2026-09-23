#!/usr/bin/env bash
# Builds and starts the staging/demo stack (MySQL, Mailpit, API, web) with Docker Compose.
#
# Usage:
#   scripts/staging-up.sh [--no-build] [--env-file PATH] [extra docker compose "up" args...]
#
#   --no-build        use existing images (e.g. API_IMAGE/WEB_IMAGE pulled from a registry)
#   --env-file PATH   defaults to deploy/.env.staging (created from deploy/.env.staging.example with fresh
#                     secrets on first run)
#
# Environment: STAGING_PROJECT (compose project name, default optimizeall-staging)
#
# After start: web http://localhost:${WEB_PORT:-8080}, Mailpit http://localhost:${MAILPIT_UI_PORT:-8025}.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT/deploy/docker-compose.staging.yml"
ENV_FILE="$ROOT/deploy/.env.staging"
PROJECT="${STAGING_PROJECT:-optimizeall-staging}"
build_flag="--build"
extra=()

while [ $# -gt 0 ]; do
  case "$1" in
    --no-build) build_flag="--no-build" ;;
    --env-file) ENV_FILE="$2"; shift ;;
    -h|--help) sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) extra+=("$1") ;;
  esac
  shift
done

command -v docker >/dev/null 2>&1 || { echo "docker is required" >&2; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "docker compose v2 is required" >&2; exit 1; }

if [ ! -f "$ENV_FILE" ]; then
  echo "Creating $ENV_FILE from the example with freshly generated secrets"
  { cat "$ROOT/deploy/.env.staging.example"; echo; "$ROOT/scripts/generate-secrets.sh" --staging; } > "$ENV_FILE"
  chmod 600 "$ENV_FILE"
fi

compose=(docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" -p "$PROJECT")

"${compose[@]}" up -d --wait --wait-timeout 300 "$build_flag" ${extra[@]+"${extra[@]}"}

# Read display ports from the env file (simple KEY=VALUE lines; last one wins, like compose).
env_value() { grep -E "^$1=" "$ENV_FILE" | tail -n 1 | cut -d= -f2- || true; }
web_port="$(env_value WEB_PORT)"; web_port="${web_port:-8080}"
mail_port="$(env_value MAILPIT_UI_PORT)"; mail_port="${mail_port:-8025}"
"${compose[@]}" ps
cat <<EOF

Staging stack is up.
  Web app   http://localhost:${web_port}
  API docs  http://localhost:${web_port}/api/docs
  Health    http://localhost:${web_port}/health/ready
  Mailpit   http://localhost:${mail_port}
  Admin     BOOTSTRAP_ADMIN_EMAIL / BOOTSTRAP_ADMIN_PASSWORD from ${ENV_FILE#"$ROOT/"}
  Demo      accounts from the Demo seed (see docs/DEMO.md)
Logs:  docker compose -p $PROJECT logs -f api
Stop:  scripts/staging-down.sh
EOF
