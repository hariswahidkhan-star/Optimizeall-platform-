#!/usr/bin/env bash
# Serves a built frontend (frontend/dist) with the production nginx configuration (frontend/nginx/default.conf.template
# and snippets), exactly as the web image does, but with a local nginx binary: no Docker needed. Used by
# scripts/e2e-journeys.sh (E2E_WEB_SERVER=nginx) to exercise the production path — server-rendered pages through SSI,
# SEO files, portal noindex headers, caching — and handy for checking docs/SEO_CRO.md by hand.
#
# Usage: scripts/serve-web-nginx.sh PORT API_UPSTREAM [WORK_DIR]
#   PORT          port to listen on (127.0.0.1 only)
#   API_UPSTREAM  API base URL, e.g. http://127.0.0.1:5099
#   WORK_DIR      where the rendered configuration, pid file and logs go (default: a new temp directory)
# Runs nginx in the foreground (stop it with SIGTERM / SIGQUIT). Needs nginx ≥ 1.18 on PATH (SSI is built in).
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

PORT="${1:?port}"
API_UPSTREAM="${2:?API upstream, e.g. http://127.0.0.1:5099}"
WORK_DIR="${3:-$(mktemp -d "${TMPDIR:-/tmp}/oa-nginx.XXXXXX")}"
DIST="$FRONTEND_DIR/dist"
NGINX_BIN="${NGINX_BIN:-nginx}"

have "$NGINX_BIN" || die "nginx is required (apt-get install nginx)"
[ -f "$DIST/index.html" ] || die "Build the frontend first (npm run build): $DIST/index.html is missing"
[ -f "$DIST/__shell/head.html" ] || die "$DIST/__shell/head.html is missing: the seoShell Vite plugin did not run"
mkdir -p "$WORK_DIR/snippets" "$WORK_DIR/tmp"

# Same substitution as the nginx image's envsubst step: only these variables, nothing else.
sed -e "s|\${API_UPSTREAM}|$API_UPSTREAM|g" -e "s|\${IMG_SRC_EXTRA}||g" \
    -e "s|listen       8080;|listen       127.0.0.1:$PORT;|" \
    -e "s|root  /usr/share/nginx/html;|root  $DIST;|" \
    -e "s|/etc/nginx/snippets/|$WORK_DIR/snippets/|g" \
    "$FRONTEND_DIR/nginx/default.conf.template" > "$WORK_DIR/default.conf"
cp "$FRONTEND_DIR"/nginx/snippets/*.conf "$WORK_DIR/snippets/"

MIME_TYPES="/etc/nginx/mime.types"
[ -f "$MIME_TYPES" ] || die "$MIME_TYPES not found"
cat > "$WORK_DIR/nginx.conf" <<EOF
worker_processes 1;
daemon off;
pid $WORK_DIR/nginx.pid;
error_log $WORK_DIR/error.log warn;
events { worker_connections 1024; }
http {
    include $MIME_TYPES;
    default_type application/octet-stream;
    access_log $WORK_DIR/access.log;
    client_body_temp_path $WORK_DIR/tmp/client;
    proxy_temp_path $WORK_DIR/tmp/proxy;
    fastcgi_temp_path $WORK_DIR/tmp/fastcgi;
    uwsgi_temp_path $WORK_DIR/tmp/uwsgi;
    scgi_temp_path $WORK_DIR/tmp/scgi;
    sendfile on;
    keepalive_timeout 65;
    include $WORK_DIR/default.conf;
}
EOF
"$NGINX_BIN" -t -p "$WORK_DIR" -c "$WORK_DIR/nginx.conf" -e "$WORK_DIR/error.log"
log "nginx on http://127.0.0.1:$PORT → API $API_UPSTREAM (config in $WORK_DIR)"
exec "$NGINX_BIN" -p "$WORK_DIR" -c "$WORK_DIR/nginx.conf" -e "$WORK_DIR/error.log"
