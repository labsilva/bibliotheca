# JavaScript extractors

Bibliotheca loads enabled extractor packages when the API starts. Packages can
supply book metadata and covers, search and resolve editions, and enrich
author profiles. Each package declares its capabilities and HTTPS origins.

## Package discovery

Install each provider in an immediate subdirectory of the configured
extractor directory. The directory name matches the manifest ID. The
manifest and entry script live at the package root. A package may also
include a README for provider-specific setup and validation.

The backend inventories installed manifests at startup and loads only
enabled packages. A missing or invalid enabled package fails startup
with a configuration error. Capabilities and deployment priorities
determine which adapters are registered for the package. Packages
with an authenticated API use a backend-approved connector.

## Use the package registry

The registry assembles the ISBN, edition-search and author providers.
Install or modify a package in the extractor directory, approve its origins
in deployment settings and restart the API. Set
`Extractors__UseBuiltInProviders=false` to run only enabled packages.
The default keeps the existing providers for capabilities that have no
enabled package. During migration, `Extractors__FallbackToBuiltIn=true`
can retry the same provider on a timeout or HTTP request failure when a
built-in equivalent is available. ISBN mismatches and invalid results
remain rejected.

For authoring and validation, use the [extractor authoring skill](../.agents/skills/bibliotheca-create-extractor/SKILL.md).

Capabilities are declared in `manifest.json`. Browser packages can implement
`metadata`, `cover`, `editionSearch`, `editionResolve` and `authorBiography`.
Edition operations receive a search or resolution request and return an
array of edition records. Edition IDs stay stable even when a product
slug changes. Author packages use `host.authorEvidence(profileUrl, name)`
on the edition page before opening the profile. The host records the
observed credit and the backend associates it with the final result.

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
      Extractors__Providers__bertrand__ResourceOrigins__1: https://img.bertrand.pt
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

The Wook package looks up metadata and covers by ISBN, searches by title
and author, and resolves existing Wook edition identifiers. It checks the product
ISBN before downloading a cover. The metadata and cover priorities default
to 5 and 0 respectively. Set `Priority` and `CoverPriority` under the
provider settings to override them. The author profile operation can be
enabled separately.

Replace `extractors/wook/extract.js` and restart the API to update the pilot.
Restore the previous script and restart to roll back, or remove the Wook
enable setting and recreate the service to return to the built-in fetchers.

## Enable the Goodreads pilot

Mount the same extractor directory and enable the Goodreads package.

```yaml
environment:
  Extractors__Providers__goodreads__Enabled: "true"
  Extractors__Providers__goodreads__NavigationOrigins__0: https://www.goodreads.com
  Extractors__Providers__goodreads__ResourceOrigins__0: https://www.goodreads.com
  Extractors__Providers__goodreads__ResourceOrigins__1: https://images-na.ssl-images-amazon.com
  Extractors__Providers__goodreads__ResourceOrigins__2: https://i.gr-assets.com
```

The package opens the edition details and verifies the returned ISBN before
fetching the cover. It uses structured contributor credits when available.
CDN approvals are optional for metadata-only deployments. The cover operation
is activated by default when at least one cover origin is approved. Deployments
can select a different subset with `EnabledOperations`, for example
`Extractors__Providers__goodreads__EnabledOperations__0=metadata`. Goodreads
responses that require an access challenge still need live validation.

## Enable the Amazon and ISBNdb cover pilots

Enable either pilot with the following origin approvals. The Amazon pilot
requires a search-result ASIN matching the requested ISBN or its equivalent
ISBN-10. ISBNdb requires a matching ISBN on the product page. Both open the
image through Playwright and preserve their existing fallback priorities.

```yaml
environment:
  Extractors__Providers__amazon__Enabled: "true"
  Extractors__Providers__amazon__NavigationOrigins__0: https://www.amazon.com
  Extractors__Providers__amazon__ResourceOrigins__0: https://m.media-amazon.com
  Extractors__Providers__amazon__ResourceOrigins__1: https://images-na.ssl-images-amazon.com
  Extractors__Providers__isbndb__Enabled: "true"
  Extractors__Providers__isbndb__NavigationOrigins__0: https://isbndb.com
  Extractors__Providers__isbndb__ResourceOrigins__0: https://isbndb.com
  Extractors__Providers__isbndb__ResourceOrigins__1: https://images.isbndb.com
```

## Enable the Hardcover edition pilot

Keep the [Hardcover search settings and deployment secret](../README.md#external-edition-search)
and add these extractor settings.

```yaml
environment:
  Extractors__Providers__hardcover__Enabled: "true"
  Extractors__Providers__hardcover__ResourceOrigins__0: https://api.hardcover.app
```

The script runs in Jint for title and author searches and edition resolution.
Its `host.hardcover()` calls select from fixed backend operations. The backend
owns the GraphQL queries, endpoint, credentials, rate limit and edition
records. The script receives candidate references and can only select
records returned for that search. This pilot uses HTTP and does not start
Playwright. Removing the extractor setting restores the built-in ordering.

## Enable the Open Library and Google Books packages

Enable the corresponding providers in the existing external edition
search settings, then enable their extractor packages. Google Books still
uses the key supplied by the deployment secret store.

```yaml
environment:
  Extractors__Providers__openlibrary__Enabled: "true"
  Extractors__Providers__openlibrary__ResourceOrigins__0: https://openlibrary.org
  Extractors__Providers__openlibrary__ResourceOrigins__1: https://en.wikipedia.org
  Extractors__Providers__googlebooks__Enabled: "true"
  Extractors__Providers__googlebooks__ResourceOrigins__0: https://www.googleapis.com
```

These API packages can filter and order verified edition or author records
through their scripts. The built-in connectors keep upstream requests,
credentials, identity checks and source identifiers under backend control.

## Enable author biographies

Once a provider is enabled, opt in to biography extraction independently by
setting `Extractors__Providers__wook__AuthorBiographyEnabled`,
`Extractors__Providers__bertrand__AuthorBiographyEnabled` or
`Extractors__Providers__goodreads__AuthorBiographyEnabled` to `true`.
Run an author refresh to exercise the pilot.

The `authorBiography` operation receives a name and up to three known editions.
It follows a matching credit from a verified edition to its profile and returns
the name, biography and edition evidence. The backend validates the evidence,
takes the source URL from the observed navigation and records plain text.
Multiple distinct profiles for the same name produce an ambiguous result.
The worker continues to apply the existing refresh and fallback policies.
Goodreads fills a missing biography after the other sources have been tried.
Wook and Bertrand can also supply a photo URL from an approved resource origin.

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
`navigationOrigins` and `resourceOrigins`. Version 1 supports the browser
capabilities described under [Use the package registry](#use-the-package-registry)
and the author profile capability through the Open Library connector.
Invalid enabled manifests prevent startup so configuration errors can be
corrected before serving traffic.

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
the initiating origin. Explicit `host.goto` commits the permitted final HTML URL. Page-initiated and
resource redirects can still retain the initiating URL in Chromium. Intermediate
redirect cookies are not replayed. Service workers and WebSockets are blocked.
The backend owns the execution deadline, result validation, priority,
fallback and persistence.

## Check a package without writing to the catalogue

Run the check with the deployment configuration and package approvals, from
the application content directory. The command loads the normal extractor
registry in package-only mode, runs one capability, and exits before the
web server or database initialization starts.

```sh
dotnet Bibliotheca.Backend.dll extractors check bertrand cover --isbn 9789897417931
dotnet Bibliotheca.Backend.dll extractors check wook editionSearch --title Matilda --author "Roald Dahl"
dotnet Bibliotheca.Backend.dll extractors check wook authorBiography --name "Roald Dahl" --isbn 9789897417931
```

On the Raspberry Pi, run the check inside the already running application
container so it can reach the deployed Playwright service. For the split
Compose deployment, use:

```sh
docker compose exec -T -w /app api dotnet Bibliotheca.Backend.dll extractors check bertrand cover --isbn 9789897417931
```

For the single-container deployment, use the `app` service and execute the
command as the application user with `docker compose exec -T -u app -w /app app`.

Use `extractors help` for the other operations. The command writes a JSON
summary to stdout and diagnostics to stderr. Exit status 0 means a match, 1
means a failed check, 2 means invalid input or missing configuration, and 3
means no match or an ambiguous author. Edition searches use the existing
provider settings and secrets. Use the check before and after replacing a
script, then verify persistence by running the normal application flow.

## Diagnose an extraction

The metadata and cover services log attempts and the provider that supplied
each result. Set the `Bibliotheca.Backend.Extractors.ExtractorRuntime`
logger to Debug to see evaluation timing and policy decisions, or Trace to
include host operation names and response lengths. The logs omit page
contents and credentials.

The VM runs on a dedicated thread, while asynchronous browser operations
run separately. This keeps the Jint allocation budget independent of
HTML and network processing on the host. Up to four evaluations can run
at once. Waiting for a slot counts toward the execution deadline.

The backend test suite exercises the manifest, redirect policy and result
contract. Install the Chromium version that matches the backend Playwright
package and set `BIBLIOTHECA_EXTRACTOR_BROWSER_TESTS=1` to also run the
browser extractor tests with local HTTP response fixtures.
