#!/usr/bin/env bash
# One-time local development setup: checks prerequisites, creates the local MySQL database and user if
# missing, restores .NET packages and installs frontend dependencies.
#
# Usage:
#   scripts/dev-setup.sh [--skip-db] [--skip-backend] [--skip-frontend] [--playwright]
#
#   --skip-db         don't touch MySQL
#   --skip-backend    don't run `dotnet restore`
#   --skip-frontend   don't run `npm ci`
#   --playwright      also install the Playwright Chromium browser for `npm run e2e`
#
# Environment (defaults match appsettings.Development.json):
#   DB_HOST=127.0.0.1 DB_PORT=3306 DB_NAME=optimizeall_dev DB_USER=optimizeall DB_PASSWORD=optimizeall_dev
#   MYSQL_ADMIN_USER=root          MySQL account used to create the database/user
#   MYSQL_ADMIN_PASSWORD=          its password (empty = none / auth_socket)
#   MYSQL_ADMIN_HOST=              empty = local socket (typical for root with auth_socket); else TCP host
#
# The app user gets ALL on `<DB_NAME>`, `optimizeall_%` (dev/e2e/openapi databases) and `oa_test_%`
# (integration tests create and drop one database per test class).
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

skip_db=false; skip_backend=false; skip_frontend=false; playwright=false
for arg in "$@"; do
  case "$arg" in
    --skip-db) skip_db=true ;;
    --skip-backend) skip_backend=true ;;
    --skip-frontend) skip_frontend=true ;;
    --playwright) playwright=true ;;
    -h|--help) sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "Unknown option: $arg (see --help)" ;;
  esac
done

# ------------------------------------------------------------------ prerequisites
log "Checking prerequisites"
missing=0
if have dotnet; then
  sdks="$(dotnet --list-sdks 2>/dev/null || true)"
  if printf '%s\n' "$sdks" | grep -q '^8\.'; then ok ".NET SDK 8 ($(printf '%s\n' "$sdks" | grep '^8\.' | tail -1 | cut -d' ' -f1))"
  else warn ".NET SDK 8.x not found (have: $(printf '%s' "$sdks" | cut -d' ' -f1 | tr '\n' ' ')). Install from https://dot.net"; missing=1; fi
else
  warn "dotnet not found. Install the .NET 8 SDK from https://dot.net"; missing=1
fi
if have node; then
  node_major="$(node -p 'process.versions.node.split(".")[0]')"
  if [ "$node_major" -ge 22 ]; then ok "Node.js $(node --version)"
  elif [ "$node_major" -ge 20 ]; then warn "Node.js $(node --version) works but 22 LTS is the supported version"
  else warn "Node.js $(node --version) is too old; install Node 22 LTS"; missing=1; fi
else
  warn "node not found. Install Node.js 22 LTS"; missing=1
fi
# check_tool CMD required|optional MESSAGE_IF_MISSING
check_tool() {
  if have "$1"; then ok "$1"
  else
    warn "$1 not found ($3)"
    if [ "$2" = required ]; then missing=1; fi
  fi
}
check_tool npm required "installed with Node.js"
check_tool mysql optional "needed for automatic database setup"
check_tool curl required "used by dev-start/test-all/export-openapi"
check_tool openssl optional "used by generate-secrets.sh"
check_tool docker optional "needed only for the staging stack"
if have dotnet && dotnet ef --version >/dev/null 2>&1; then ok "dotnet-ef"
else warn "dotnet-ef not found (optional, for adding migrations): dotnet tool install --global dotnet-ef --version 8.0.10"; fi
[ "$missing" -eq 0 ] || die "Install the missing prerequisites above and re-run."

# ------------------------------------------------------------------ database
if ! $skip_db; then
  log "Ensuring MySQL database '$DB_NAME' and user '$DB_USER'"
  have mysql || die "mysql client is required for database setup (or re-run with --skip-db and create it manually; see docs/DEPLOYMENT.md)"

  admin_args=(-u "${MYSQL_ADMIN_USER:-root}")
  [ -n "${MYSQL_ADMIN_HOST:-}" ] && admin_args+=(--protocol=TCP -h "$MYSQL_ADMIN_HOST" -P "$DB_PORT")

  # Identifiers/passwords are validated to keep the generated SQL safe.
  [[ "$DB_NAME" =~ ^[A-Za-z0-9_]+$ ]] || die "DB_NAME may only contain letters, digits and _"
  [[ "$DB_USER" =~ ^[A-Za-z0-9_]+$ ]] || die "DB_USER may only contain letters, digits and _"
  [[ "$DB_PASSWORD" != *"'"* && "$DB_PASSWORD" != *"\\"* ]] || die "DB_PASSWORD may not contain quotes or backslashes"

  sql="CREATE DATABASE IF NOT EXISTS \`$DB_NAME\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;"
  for host in localhost 127.0.0.1; do
    sql+="
CREATE USER IF NOT EXISTS '$DB_USER'@'$host' IDENTIFIED BY '$DB_PASSWORD';
GRANT ALL PRIVILEGES ON \`$DB_NAME\`.* TO '$DB_USER'@'$host';
GRANT ALL PRIVILEGES ON \`optimizeall\\_%\`.* TO '$DB_USER'@'$host';
GRANT ALL PRIVILEGES ON \`oa\\_test\\_%\`.* TO '$DB_USER'@'$host';"
  done

  if ! MYSQL_PWD="${MYSQL_ADMIN_PASSWORD:-}" mysql "${admin_args[@]}" -e "$sql"; then
    die "Could not create the database/user as MySQL admin '${MYSQL_ADMIN_USER:-root}'. Set MYSQL_ADMIN_USER/MYSQL_ADMIN_PASSWORD/MYSQL_ADMIN_HOST, or run the SQL in docs/DEPLOYMENT.md manually."
  fi

  if mysql_app -e "SELECT 1" "$DB_NAME" >/dev/null; then
    ok "Connected to $DB_NAME as $DB_USER@$DB_HOST:$DB_PORT"
  else
    die "Database exists but '$DB_USER' cannot connect with DB_PASSWORD over TCP. If the user already existed with another password, reset it: ALTER USER '$DB_USER'@'127.0.0.1' IDENTIFIED BY '...';"
  fi
fi

# ------------------------------------------------------------------ backend
if ! $skip_backend; then
  log "Restoring .NET packages"
  dotnet restore "$BACKEND_DIR/OptimizeAll.sln"
fi

# ------------------------------------------------------------------ frontend
if ! $skip_frontend; then
  if [ -f "$FRONTEND_DIR/package.json" ]; then
    log "Installing frontend dependencies (npm ci)"
    (cd "$FRONTEND_DIR" && npm ci --no-audit --no-fund)
    if $playwright; then
      log "Installing Playwright Chromium"
      (cd "$FRONTEND_DIR" && npx playwright install chromium)
    fi
  else
    warn "frontend/package.json not found; skipping npm ci"
  fi
fi

ensure_dev_dir
log "Done. Start everything with scripts/dev-start.sh"
