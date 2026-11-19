# syntax=docker/dockerfile:1
# Web image of the demo environment (I20, ADR-0012): the static Vite build served by Caddy, which also terminates HTTPS
# and proxies /api, /fake-checkout and the health endpoints to the API container.
#   docker build -f deploy/docker/web.Dockerfile .
# The build context is the repository root.

FROM node:24-alpine AS build
WORKDIR /web
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/web/ ./
RUN npm run build

FROM caddy:2.11-alpine
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /web/dist /srv
