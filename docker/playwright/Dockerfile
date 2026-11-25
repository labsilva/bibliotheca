ARG PLAYWRIGHT_VERSION=1.65.1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/playwright:v${PLAYWRIGHT_VERSION}-noble AS dependencies
ARG PLAYWRIGHT_VERSION
ENV NODE_ENV=production
WORKDIR /opt/bibliotheca-browser
RUN npm install --omit=dev --ignore-scripts --no-audit --no-fund playwright@${PLAYWRIGHT_VERSION}

FROM mcr.microsoft.com/playwright:v${PLAYWRIGHT_VERSION}-noble
RUN apt-get update \
    && apt-get upgrade -y \
    && rm -rf /var/lib/apt/lists/*
ENV NODE_ENV=production
WORKDIR /opt/bibliotheca-browser
# Playwright's npm packages are JavaScript; Chromium and Node come from the
# target-architecture image.
COPY --from=dependencies /opt/bibliotheca-browser/node_modules ./node_modules
COPY server.mjs egress.mjs control.mjs check.mjs check-security.mjs probe-wook.mjs ./
USER pwuser
EXPOSE 8080 8081
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 CMD node -e "fetch('http://127.0.0.1:8080/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"
CMD ["xvfb-run", "-a", "--server-args=-screen 0 1280x800x24 -nolisten tcp", "node", "server.mjs"]
