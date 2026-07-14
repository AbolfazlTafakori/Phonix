# syntax=docker/dockerfile:1

# ---- deps ----
FROM node:20-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

# ---- build ----
FROM node:20-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
# NEXT_PUBLIC_* is inlined into the client bundle at build time, so this must be the PUBLIC API URL
# the browser will hit (your domain or server IP), not the internal docker hostname.
ARG NEXT_PUBLIC_API_URL=http://localhost:5228
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

# ---- runtime ----
FROM node:20-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
RUN addgroup -g 1001 -S nodejs && adduser -S nextjs -u 1001

# Standalone output bundles only the files the server needs; static assets + public are copied alongside.
COPY --from=build /app/public ./public
COPY --from=build --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=build --chown=nextjs:nodejs /app/.next/static ./.next/static

# The image optimizer writes its re-encoded (AVIF/WebP) variants here. Without a writable cache it would
# re-encode on every request instead of once per image.
RUN mkdir -p .next/cache/images && chown -R nextjs:nodejs .next

USER nextjs
EXPOSE 3000
CMD ["node", "server.js"]
