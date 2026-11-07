# syntax=docker/dockerfile:1
#
# The Next.js web app as a standalone server. Build from web/: docker build web
# Everything it needs is read at runtime from the environment (see the repo's .env.example), so one image fits every
# environment.

FROM node:22.23.4-alpine3.24 AS base
ENV NEXT_TELEMETRY_DISABLED=1 \
    COREPACK_ENABLE_DOWNLOAD_PROMPT=0
# The npm version pinned in package.json's packageManager, as CI uses.
RUN corepack enable npm

FROM base AS deps
WORKDIR /app
COPY package.json package-lock.json ./
# HUSKY=0: no git hooks to install in an image.
RUN HUSKY=0 npm ci

FROM base AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN npm run build

FROM node:22.23.4-alpine3.24 AS web
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
COPY --from=build --chown=node:node /app/public ./public
USER node
EXPOSE 3000
CMD ["node", "server.js"]
