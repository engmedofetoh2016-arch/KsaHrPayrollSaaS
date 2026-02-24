# syntax=docker/dockerfile:1

FROM node:20-alpine AS build
WORKDIR /app

COPY src/web/package*.json ./
RUN npm ci

COPY src/web/ ./
RUN npm run build

FROM nginx:1.27-alpine AS final
WORKDIR /usr/share/nginx/html

COPY src/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/hrpayroll-web/browser ./
COPY src/web/docker-entrypoint.sh /docker-entrypoint.sh

RUN chmod +x /docker-entrypoint.sh

EXPOSE 80

ENTRYPOINT ["/docker-entrypoint.sh"]
