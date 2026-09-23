#!/usr/bin/env bash
# Stops the staging/demo stack.
#
# Usage:
#   scripts/staging-down.sh [--volumes] [--env-file PATH]
#
#   --volumes         also delete the MySQL data and API storage volumes (full reset; the next start
#                     re-runs migrations and the demo seed on an empty database)
#   --env-file PATH   defaults to deploy/.env.staging
#
# Environment: STAGING_PROJECT (compose project name, default optimizeall-staging)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT/deploy/docker-compose.staging.yml"
ENV_FILE="$ROOT/deploy/.env.staging"
PROJECT="${STAGING_PROJECT:-optimizeall-staging}"
down_args=(--remove-orphans)

while [ $# -gt 0 ]; do
  case "$1" in
    -v|--volumes) down_args+=(--volumes) ;;
    --env-file) ENV_FILE="$2"; shift ;;
    -h|--help) sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

# `down` does not need real secrets, but compose still interpolates the file; fall back to the example.
[ -f "$ENV_FILE" ] || ENV_FILE="$ROOT/deploy/.env.staging.example"

docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" -p "$PROJECT" down "${down_args[@]}"
