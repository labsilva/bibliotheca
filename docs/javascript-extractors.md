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

## Enable the Wook pilot

Use the same read-only extractor mount and add these settings to the API service.

```yaml
environment:
  Extractors__Providers__wook__Enabled: "true"
  Extractors__Providers__wook__NavigationOrigins__0: https://www.wook.pt
  Extractors__Providers__wook__ResourceOrigins__0: https://www.wook.pt
  Extractors__Providers__wook__ResourceOrigins__1: https://img.wook.pt
```

The Wook pilot looks up metadata and covers by ISBN. It checks the product
ISBN before downloading a cover. The metadata and cover priorities default
to 5 and 0 respectively. Set `Priority` and `CoverPriority` under the
provider settings to override them. Wook's existing author and external
edition providers still handle those operations.

Replace `extractors/wook/extract.js` and restart the API to update the pilot.
Restore the previous script and restart to roll back, or remove the Wook
enable setting and recreate the service to return to the built-in fetchers.

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
`synopsis`, `language`, `pages` (a positive integer), `subtitle`,
`collection`, `genres` (an array of strings) and `hasStructuredAuthors`. For a cover,
call `host.downloadImage` before returning the matching metadata. The
backend tracks the source URL and the downloaded bytes.

- `await host.goto(url)` fetches an HTML document and returns its final URL
  or `null` for HTTP 404. Relative URLs resolve against the current document.
- `await host.fill(selector, value)` fills a form field.
- `await host.click(selector)` clicks an element.
- `await host.waitForSelector(selector)` waits for an element to become visible.
- `await host.text(selector)` returns an array of matching text values.
- `await host.attribute(selector, name)` returns an array of matching
  attribute values in document order.
- `await host.downloadImage(url)` downloads an approved raster image.
  It uses the current Playwright page and its cookies. It returns a boolean
  and is available during cover lookups. Pass `"navigate"` as the second
  argument to open the image in a separate page in the same context, with
  the product URL as the referrer. Both modes use resource origins and
  reject redirects.

Use scripts you trust and review before installation. Each execution gets a
Jint engine and an offline Playwright context with page JavaScript enabled.
Playwright routes document requests, including forms and iframes, through the
manifest's navigation origins. Other requests use the resource origins.
The backend performs the HTTPS requests, bounds the responses, and checks each
redirect destination before sending a request. Browser redirects can stay on
the initiating origin. Chromium retains the original request URL for a relayed
redirect, so use explicit navigation for pages that depend on the redirected
URL for relative links or scripts. Service workers and WebSockets are blocked.
The backend owns the execution deadline, result validation, priority,
fallback and persistence.

The backend test suite exercises the manifest, redirect policy and result
contract. Install the Chromium version that matches the backend Playwright
package and set `BIBLIOTHECA_EXTRACTOR_BROWSER_TESTS=1` to also run the
Bertrand and Wook browser tests with local HTTP response fixtures.
