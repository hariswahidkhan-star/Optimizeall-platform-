# syntax=docker/dockerfile:1.7
#
# Optimize All web image: Vite build served by unprivileged nginx, which also reverse-proxies the API
# (/api/, /t/, /health/) so the browser talks to a single origin (no CORS, SameSite=Strict refresh cookie works).
#
# Build context: frontend/
#   docker build -t optimizeall-web frontend
#   docker run -p 8080:8080 -e API_UPSTREAM=http://api:8080 optimizeall-web
#
# Runtime env:
#   API_UPSTREAM   base URL of the API (scheme://host:port, no path). Default http://api:8080.

ARG NODE_VERSION=22
ARG NGINX_VERSION=1.27

# ---------------------------------------------------------------------------------------------------------
FROM node:${NODE_VERSION}-alpine AS build
WORKDIR /app
ENV CI=true

# Dependency layer (cached until the lockfile changes).
COPY package.json package-lock.json ./
RUN --mount=type=cache,id=npm,target=/root/.npm \
    npm ci --no-audit --no-fund

COPY . .
RUN npm run build

# ---------------------------------------------------------------------------------------------------------
FROM nginxinc/nginx-unprivileged:${NGINX_VERSION}-alpine AS runtime

# The official image renders /etc/nginx/templates/*.template with envsubst into /etc/nginx/conf.d at startup.
# Only variables that are defined in the environment are substituted, so nginx's own $vars are untouched.
ENV API_UPSTREAM=http://api:8080

COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY nginx/snippets/ /etc/nginx/snippets/
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080
USER 101

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1
