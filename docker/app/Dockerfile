ARG PLAYWRIGHT_VERSION=1.65.1
FROM --platform=$BUILDPLATFORM node:26.13.1-alpine AS frontend
WORKDIR /src
COPY src/Bibliotheca.Frontend/package*.json ./
RUN npm ci
COPY src/Bibliotheca.Frontend/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS backend
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet publish src/Bibliotheca.Backend/Bibliotheca.Backend.csproj -c Release -a $TARGETARCH --self-contained false -o /output

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS dotnet
FROM --platform=$BUILDPLATFORM node:26.13.1-alpine AS browser-packages
ARG PLAYWRIGHT_VERSION
WORKDIR /browser
RUN npm install --omit=dev --ignore-scripts --no-audit --no-fund playwright@${PLAYWRIGHT_VERSION}

FROM mcr.microsoft.com/playwright:v${PLAYWRIGHT_VERSION}-noble
RUN apt-get update \
    && apt-get upgrade -y \
    && apt-get install -y --no-install-recommends nginx tini curl libatomic1 libicu74 libssl3t64 libstdc++6 zlib1g \
    && rm -rf /var/lib/apt/lists/* /etc/nginx/sites-enabled/default
# Copy the versioned .NET runtime instead of depending on a distribution package feed.
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet PATH="/usr/share/dotnet:${PATH}" \
    ASPNETCORE_ENVIRONMENT=Production Deployment__Combined=true \
    PLAYWRIGHT_HOST=127.0.0.1 Playwright__Remote=http://127.0.0.1:8080/endpoint
WORKDIR /app
COPY --from=backend /output/ ./
COPY --from=frontend /src/dist/ /usr/share/nginx/html/
COPY --from=browser-packages /browser/node_modules /opt/bibliotheca-browser/node_modules
COPY docker/playwright/server.mjs /opt/bibliotheca-browser/server.mjs
COPY docker/playwright/egress.mjs /opt/bibliotheca-browser/egress.mjs
COPY docker/web/runtime-config.mjs /opt/bibliotheca/web/runtime-config.mjs
COPY docker/app/configure.mjs /opt/bibliotheca/app/configure.mjs
COPY docker/app/entrypoint.sh docker/app/health.sh /opt/bibliotheca/
RUN sed -i 's/\r$//' /opt/bibliotheca/*.sh && chmod +x /opt/bibliotheca/*.sh
# Keep API data and identity keys inaccessible to Chromium's distinct pwuser.
RUN groupadd --gid 1654 app && useradd --uid 1654 --gid app --no-create-home app \
    && mkdir -p /app/data /app/temp && chown app:app /app/data /app/temp \
    && chmod 700 /app/data /app/temp \
    && mkdir -p /var/lib/nginx/body /var/lib/nginx/proxy /var/lib/nginx/fastcgi /var/lib/nginx/uwsgi /var/lib/nginx/scgi \
    && chown -R www-data:www-data /var/lib/nginx \
    && sed -i 's|error_log .*;|error_log /dev/stderr;|; s|access_log .*;|access_log off;|' /etc/nginx/nginx.conf
EXPOSE 80 443
HEALTHCHECK --interval=30s --timeout=10s --start-period=90s --retries=3 CMD ["/opt/bibliotheca/health.sh"]
ENTRYPOINT ["/usr/bin/tini", "--", "/opt/bibliotheca/entrypoint.sh"]
