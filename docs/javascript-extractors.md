# JavaScript extractors

Bibliotheca loads enabled extractors when the API starts. The Bertrand pilot supplies
book metadata and covers by ISBN. Author refresh continues to use the existing
provider. If no JavaScript extractor is enabled, the existing fetchers are used.

## Install the Bertrand extractor

Mount the repository's `extractors` directory read-only at `/app/extractors`.
Add the following settings to the API service in a Compose override. For the
single-container topology, use the `app` service name.

```yaml
services:
  api:
    volumes:
      - ./extractors:/app/extractors:ro
    environment:
      Extractors__Directory: /app/extractors
      Extractors__Providers__bertrand__Enabled: "true"
      Extractors__Providers__bertrand__Priority: "6"
      Extractors__Providers__bertrand__NavigationOrigins__0: https://www.bertrand.pt
      Extractors__Providers__bertrand__ResourceOrigins__0: https://www.bertrand.pt
```

Apply the override using the same Compose files and project name as the
deployment. The API must be able to read the manifest and script and resolve
and reach the approved hosts over HTTPS. The API's existing Playwright
settings determine where the browser runs. Approval is by exact HTTPS origin
and by request type. Add each required origin to both the appropriate manifest
list and the deployment settings. Lower priorities run first.

## Update or roll back on the Raspberry Pi

Keep a copy of the currently installed script. Replace only
`extractors/bertrand/extract.js` and restart the API service using
`docker compose restart api` with the deployment's Compose files. For the
single-container topology, restart `app`. Compare the container image
ID before and after the restart and verify an ISBN lookup and cover import.
Restore the previous script and restart to roll back. To return to the
original Bertrand fetchers, remove the provider's enable setting and recreate
the service with the updated Compose configuration.

## Version 1 contract

Each enabled provider directory contains `manifest.json` and `extract.js`.
The manifest declares `id`, `contractVersion`, `entry`, `operations`,
`navigationOrigins` and `resourceOrigins`. Version 1 accepts the
`metadata` and `cover` operations. Invalid enabled manifests prevent
startup, so configuration errors can be corrected before serving traffic.

The script defines `async function extract(host, operation, isbn)`.
The input ISBN is a canonical ISBN-13. Return `null` when there is no match.
A matching result contains `isbn` and `title`. Optional fields include
`authors` (an array of names), `publisher`, `publishedOn` (YYYY-MM-DD),
`synopsis`, `language` and `pages` (a positive integer). For a cover,
call `host.downloadImage` before returning the matching metadata. The
backend tracks the source URL and the downloaded bytes.

- `await host.goto(url)` fetches an HTML document and returns its final URL
  or `null` for HTTP 404. Relative URLs resolve against the current document.
- `await host.text(selector)` returns an array of matching text values.
- `await host.attribute(selector, name)` returns an array of matching
  attribute values in document order.
- `await host.downloadImage(url)` downloads an approved raster image.
  It returns a boolean and is available during cover lookups.

Use scripts you trust and review before installation. Each execution gets a
Jint engine and an offline Playwright context. The backend fetches bounded
responses over HTTPS and checks origin approval at each redirect hop before
sending a request. The browser inspects a static copy of the HTML with page
scripts and service workers disabled. Provider pages that require client-side
execution, cookies, or interactive navigation are outside this pilot.
The backend owns the execution deadline, result validation, priority,
fallback and persistence.

The backend test suite exercises the manifest, redirect policy and result
contract. Install the Chromium version that matches the backend Playwright
package and set `BIBLIOTHECA_EXTRACTOR_BROWSER_TESTS=1` to also run the
Bertrand browser test with local HTTP response fixtures.
