#!/usr/bin/env bash
# Checks the production nginx configuration (frontend/nginx/default.conf.template) against a stub API: every public
# page answers text/html whatever the API returns (JSON errors, rate limiting, API down), so no browser ever offers a
# page as a download, while the API's own HTML statuses (200/301/404/410) and the SEO files pass through unchanged.
#
# Usage: scripts/test-web-nginx.sh        (needs nginx and curl; no API, database or frontend build)
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

have nginx || die "nginx is required (apt-get install nginx)"
work="$(mktemp -d "${TMPDIR:-/tmp}/oa-nginx-test.XXXXXX")"
chmod 755 "$work" # nginx workers run as another user when started as root
stub_pid=""; web_pid=""
cleanup() {
  [ -n "$web_pid" ] && kill "$web_pid" 2>/dev/null || true
  [ -n "$stub_pid" ] && kill "$stub_pid" 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT

free_port() { local p; for p in $(seq "$1" $(($1 + 200))); do port_in_use "$p" || { echo "$p"; return; }; done; die "no free port"; }
api_port=$(free_port 18900); web_port=$(free_port $((api_port + 1)))

# A frontend directory with the real nginx configuration and a minimal build.
mkdir -p "$work/frontend/dist/__shell" "$work/stub/tmp"
ln -s "$FRONTEND_DIR/nginx" "$work/frontend/nginx"
echo '<!doctype html><title>app shell</title><div id="root">SHELL</div>' > "$work/frontend/dist/index.html"
echo '<script type="module" src="/assets/index-test.js"></script>' > "$work/frontend/dist/__shell/head.html"
echo '' > "$work/frontend/dist/__shell/body.html"

# Stub API: /_document/<case> answers like the API would, including its JSON error responses.
{
  cat <<EOF
worker_processes 1; daemon off; pid $work/stub/nginx.pid; error_log $work/stub/error.log warn;
events { worker_connections 64; }
http {
  access_log off;
  client_body_temp_path $work/stub/tmp/c; proxy_temp_path $work/stub/tmp/p; fastcgi_temp_path $work/stub/tmp/f;
  uwsgi_temp_path $work/stub/tmp/u; scgi_temp_path $work/stub/tmp/s;
  server {
    listen 127.0.0.1:$api_port;
    default_type text/html;
    location = /_document/ { return 200 '<!doctype html><h1>API home</h1>'; }
    location = /_document/page { return 200 '<!doctype html><h1>API page</h1>'; }
    location = /_document/missing { return 404 '<!doctype html><h1>API not found</h1>'; }
    location = /_document/gone { return 410 '<!doctype html><h1>API gone</h1>'; }
    location = /_document/moved { return 301 /page; }
    location = /llms.txt { default_type text/plain; return 200 'llms'; }
    location = /_markdown/index { default_type text/markdown; return 200 '# home'; }
    location = /api/v1/public/site { default_type application/json; return 200 '{"ok":true}'; }
    # Echoes the forwarded scheme and host: the API builds links on them when no public URL is configured.
    location = /_document/origin { return 200 '<!doctype html>origin=\$http_x_forwarded_proto://\$http_x_forwarded_host'; }
    location = /api/v1/origin { default_type text/plain; return 200 'origin=\$http_x_forwarded_proto://\$http_x_forwarded_host'; }
EOF
  for code in 400 401 403 405 429 500 503; do
    echo "    location = /_document/e$code { default_type application/problem+json; return $code '{\"status\":$code}'; }"
  done
  echo "    location = /_document/nobody-type { types { } default_type \"\"; return 500 'x'; }"
  echo "  }"
  echo "}"
} > "$work/stub/nginx.conf"
nginx -p "$work/stub" -c "$work/stub/nginx.conf" -e "$work/stub/error.log" &
stub_pid=$!

FRONTEND_DIR="$work/frontend" "$ROOT/scripts/serve-web-nginx.sh" "$web_port" "http://127.0.0.1:$api_port" "$work/web" > "$work/web.log" 2>&1 &
web_pid=$!
wait_for_url "http://127.0.0.1:$web_port/healthz" 30 "$web_pid" || { cat "$work/web.log"; die "web nginx did not start"; }
wait_for_url "http://127.0.0.1:$api_port/_document/page" 30 "$stub_pid" || die "stub API did not start"

failures=0
# expect PATH STATUS CONTENT_TYPE_PREFIX [BODY_SUBSTRING]
expect() {
  local out status type body
  out=$(curl -s -o "$work/body" -w '%{http_code} %{content_type}' "http://127.0.0.1:$web_port$1")
  status=${out%% *}; type=${out#* }; body=$(cat "$work/body")
  if [ "$status" != "$2" ] || [[ "$type" != "$3"* ]] || { [ -n "${4:-}" ] && [[ "$body" != *"$4"* ]]; }; then
    warn "$1: got $status [$type], expected $2 [$3]${4:+ with [$4]}"; failures=$((failures + 1))
  else
    ok "$1 → $status $type"
  fi
}

expect /healthz 200 text/plain
expect / 200 text/html "API home"
expect /page 200 text/html "API page"
expect /missing 404 text/html "API not found"
expect /gone 410 text/html "API gone"
expect /moved 301 text/html
for code in 400 401 403 405 429 500 503; do expect "/e$code" 503 text/html SHELL; done
expect /nobody-type 503 text/html SHELL
expect /llms.txt 200 text/plain
expect /index.md 200 text/markdown
expect /api/v1/public/site 200 application/json
expect /app 200 text/html SHELL
# X-Forwarded-Proto/Host carry the visitor's scheme and host (with its port) to the API (docs/RENDER.md#public-url).
expect /origin 200 text/html "origin=http://127.0.0.1:$web_port"
expect /api/v1/origin 200 text/plain "origin=http://127.0.0.1:$web_port"

log "API down"
kill "$stub_pid"; wait "$stub_pid" 2>/dev/null || true; stub_pid=""
expect /healthz 200 text/plain
expect / 503 text/html SHELL
expect /page 503 text/html SHELL
expect /api/v1/public/site 503 application/problem+json service_unavailable

[ "$failures" = 0 ] || die "$failures nginx check(s) failed"
ok "nginx serves every page as HTML, whatever the API answers"
