# Wook external edition search

Wook is disabled until enabled in the backend. It uses the existing external
edition search settings and needs no API credential. Set these environment
variables on the API process or API container.

```text
ExternalEditionSearch__Providers__wook__Enabled=true
ExternalEditionSearch__Providers__wook__Priority=50
ExternalEditionSearch__Providers__wook__TimeoutSeconds=15
ExternalEditionSearch__Providers__wook__ResultLimit=10
```

Lower priority numbers take precedence when providers return the same ISBN.
The timeout includes any wait for Wook's browser slot. The result limit caps
the number of distinct products returned from Wook's search page. Search reads
the available listing fields. Selecting a result reads the product page and adds
its available ISBN, authors, subtitle, language, publisher, publication date,
format, collection, pages, synopsis, genre, cover URL and source link. Wook
product links identify editions whose search listing or detail lacks an ISBN.

Wook imports read each linked author separately and exclude credits marked
`Ilustração`. On API startup, the catalogue repair uses successful imports with
saved structured credits and Wook credit labels to correct confirmed legacy
book-author links. It keeps existing author profiles and photos. Startup logs
the book and author IDs for credits that need review. A metadata refresh can
correct a combined credit when the source provides the corresponding individual
authors for that edition.

## Playwright runtime

For local development, build the backend and install the Chromium version
matched to its Microsoft.Playwright package. With `Playwright:Remote` empty,
the backend launches Chromium headlessly.

The backend now retains the browser's native user-agent. Local browser launches
also support `Playwright:Channel` (for example `msedge`) and `Playwright:Headless`.
For the configuration validated on this Windows machine:

```text
Playwright__Remote=
Playwright__Channel=msedge
Playwright__Headless=false
```

This starts a fresh installed Edge session with no imported personal cookies.
Headed execution requires an interactive desktop on Windows. On Linux it needs
a display such as Xvfb and an installed compatible browser. Channel and Headless
apply to local launches; they do not reconfigure a browser connected through
`Playwright:Remote`. This Windows configuration is for local diagnostics only;
the production container configuration is described below. Local defaults remain
bundled Chromium/headless.

```powershell
dotnet build src/Bibliotheca.Backend/Bibliotheca.Backend.csproj
pwsh -NoProfile -File src/Bibliotheca.Backend/bin/Debug/net11.0/playwright.ps1 install chromium
```

On Linux, the Playwright installation also needs Chromium's operating system
dependencies. Use the matching Playwright container image or install with
`install --with-deps chromium` where the distribution and permissions allow it.

## Production: Linux ARM64 in Docker without a desktop

Compose builds `docker/playwright/Dockerfile` for the Docker host's native
architecture. Its official Playwright base image supports Linux ARM64 and amd64.
The separate browser service runs Chromium with `headless: false` under Xvfb.
Xvfb supplies a virtual display inside the container; no monitor, desktop login,
VNC server, Edge installation or human interaction is required.

The API uses `Playwright__Remote=http://playwright:8080/endpoint`. It discovers
the browser's random WebSocket path and connects on port 8081 using the same
configured host. Both ports stay on the private Compose browser network. The
service owns launch configuration; local API `Channel` and `Headless` options
do not change this remote Chromium. Existing explicit `ws://` and `wss://`
endpoints remain supported.

Playwright 1.65.1 is installed when the image is built, matching the backend's
Microsoft.Playwright version. Startup requires no npm download. A multi-stage
build installs the platform-neutral JavaScript package on the build machine and
gets Node, Chromium and OS dependencies from the target-architecture base image,
so building the ARM64 image does not require QEMU. The service runs as `pwuser`,
uses 512 MiB of shared memory, and exposes a health check. Compose waits for it
to become healthy before starting the API. Browser contexts are fresh for every
operation; clients disconnect without stopping the shared browser.

Deploy the updated API image together with this Compose file and the complete
`docker/playwright` directory. The discovery support is new backend code, so an
older API image cannot use the HTTP endpoint. On the Raspberry, after deploying
that API image:

```sh
docker compose build playwright
docker compose up -d playwright api
docker compose exec playwright node check.mjs
docker compose exec playwright node probe-wook.mjs
```

`check.mjs` verifies rendering and two independent client connections without
external requests. `probe-wook.mjs` separately checks the three real title
searches and a product detail per search, prints the HTTP outcomes and returns
a nonzero exit code on failures. Set Wook's provider variables on the API
service as shown above. Wook browser navigation is limited to one operation at
a time. To diagnose startup, use `docker compose logs --tail=50 playwright`.

For a cross-build from an amd64 workstation:

```sh
docker build --platform linux/arm64 -t bibliotheca-playwright:1.65.1 docker/playwright
```

## Live validation

A live Chromium probe on 21 November 2026 attempted these Wook searches from
the development environment. The browser reached Wook and received Cloudflare's
challenge page for each request.

| Search | HTTP status | Page title | Product links |
| --- | ---: | --- | ---: |
| Matilda | 403 | Attention Required! | 0 |
| Ensaio sobre a Cegueira | 403 | Attention Required! | 0 |
| Livro do Desassossego | 403 | Attention Required! | 0 |

A follow-up comparison on the same Windows machine and network used installed
Edge 158.0.4370.33, native user-agent and fresh contexts:

| Execution | Search | Detail |
| --- | --- | --- |
| Edge headed: Matilda | 200 | 200 |
| Edge headed: Ensaio sobre a Cegueira | 200 | 200 |
| Edge headed: Livro do Desassossego | 200 | 200 |
| Edge headless: Matilda | 403 / Cloudflare challenge | Not attempted |

The actual .NET provider was then executed with `Channel=msedge`,
`Headless=false` and no remote endpoint. It successfully searched Matilda with
author Roald Dahl and resolved the selected edition as ISBN 9789897417931.
The real saved pages for the three titles were also passed through the .NET
provider/parser; their first editions resolved to ISBNs 9789897417931,
9789720046833 and 9789723717877 respectively, with authors and detail metadata.
Search cover parsing now removes Wook's trailing image width descriptor instead
of treating it as part of the URL.

The result establishes a working local headed configuration, not a guarantee for
headless execution or for the separate container environment. No challenge was
solved and no personal browser session was reused.

After adapting the deployment, the final browser image was built for both
`linux/amd64` and `linux/arm64`. The amd64 image was run on Docker's Linux engine:
the health check, rendering fixture and repeated client connections passed.
Chromium under Xvfb returned HTTP 200 for all three Wook searches and their
selected product pages. ARM64 image construction was verified, but native ARM64
execution and Wook access from the Raspberry's network still require the above
checks on the actual Raspberry. A successful build is not evidence of native
runtime behaviour or a guarantee against future provider blocking.

The diagnostic is repeatable from `src/Bibliotheca.Frontend` using
`node e2e/probe-wook.mjs`, with optional environment variables
`WOOK_PROBE_CHANNEL=msedge`, `WOOK_PROBE_HEADLESS=false`,
`WOOK_PROBE_DETAILS=true` and `WOOK_PROBE_TITLE=Matilda`. Its default remains a
headless three-title probe. Automated tests continue to use local fixtures.

## Author profiles from Wook

When `ExternalEditionSearch:Providers:wook:Enabled` is true, author refresh also
checks up to three linked editions, prioritizing saved Wook references and then
ISBNs. For ISBN search it accepts only a unique candidate and verifies the ISBN
again on the product page. It follows a Wook author ID only when the edition's
structured credit matches the catalogue author, and rejects conflicting IDs.
Profiles without a verifiable edition are left unchanged. Each author is bounded
by the provider timeout and Wook pages share the existing serialized browser slot.

An existing photograph is retained. A Wook biography fills an empty biography or
replaces a short Open Library biography (under 250 characters) only when it is
at least twice as long. The source URL is retained and shown on the author page;
Wikipedia and other existing biographies are preserved. Ambiguous matches appear
in the refresh error report. Wook is a third-party source: check its applicable
terms before redistributing or exporting its text and images.

### Verification and coverage limits

The existing Open Library refresh requires a known author key or a linked edition
with an ISBN or Open Library reference, and only follows a Wikipedia page backed
by matching Wikidata evidence. Authors without that evidence cannot be matched.
A live browser inspection on 2026-11-22 confirmed the Wook product credit and
profile structure for the public George R. R. Martin example; the separate local
headless Chromium probe received HTTP 403. This is one example, not a representative
coverage measurement or a test of the production ARM64 browser. Measure coverage
on a representative catalogue after deployment and separately review Wook terms.
Automated tests use synthetic fixtures, not live Wook pages.
