# syntax=docker/dockerfile:1
# The native TypeScript bridge requires glibc while building the app.
FROM node:24-bookworm-slim@sha256:a5f4366e834695d5f363b920c104953a535d3a9864d1bdf74566a0ebe01daee5 AS source
WORKDIR /app
RUN chown node:node /app
USER node
COPY --chown=node:node package.json package-lock.json ./
RUN npm ci
COPY --chown=node:node . .

FROM source AS build
RUN npm run build

FROM nginx:stable-alpine@sha256:ed84151ed2d47ef93c0939eb9b9754c6990db6092cd2081e11988a47a83d47ea AS runtime
COPY nginx.conf /etc/nginx/nginx.conf
COPY --from=build /app/dist /usr/share/nginx/html
USER nginx
EXPOSE 5173
HEALTHCHECK --interval=5s --timeout=5s --start-period=10s --retries=10 \
    CMD wget --quiet --tries=1 --timeout=4 --output-document=/dev/null http://127.0.0.1:5173/ || exit 1
ENTRYPOINT ["nginx", "-g", "daemon off;"]
