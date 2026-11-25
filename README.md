## Bibliotheca
I have a fairly large library of physical books.
There are a bunch of (great) apps to manage a library of digital books, but when
I searched for one to manage a physical library, the ones I found lacked something,
either the metadata fetching didn't quite work for my books, or it was way overkill 
for my use-case, or some other thing that actually kept me from using it.

The application I wanted:

* Should manage an household library of physical books
* Every user should have a private library with as many shelves as needed
* Books could be added to the application but not necessarily to the user libraries
* Book metadata should be scoped to the user
* Books could be tagged and tags should be public
* Books that belong to a collection should be archived together somehow, and users 
should be able to control the collections

So I've built this app with these requirements in mind. It has come together quite
nicely, and works quite well for collections of under 1000 books.
I plan on adding more features in the future, but keep in mind that this is a personal
app and you might be better off just forking the project and hacking any feature you
actually need.

It works as a PWA too, but for it to work correctly (at least in iOS) you might need certificates.
There's a guide on how to create a Certificate Authority (CA) and sign certificates in 
[`the documentation`](docs/certificates.md).
It should work just fine with any certificate (Let's Encrypt for instance).

There's a ton of tech debt to be cleared, and I plan to do so in the future, but as it
stands today it works fine for my use-case, so I'm sharing it publicly for anyone who
might have the same use-case as myself and actually wants to use it.

## Build
It builds using Docker files, one for the backend and another one to the frontend.
There's a Powershell file (it's cross platform y'all) that calls the Docker command.
My aim was to have it running on a Raspberry Pi, so the build process produces images
for `linux/amd64` and `linux/arm64`.
Use `./build.ps1 -Playwright` to build and push `bibliotheca-playwright:1.65.1`
for both architectures, or `./build.ps1 -Playwright -Registry 'your-registry/your-namespace/'`
to select the registry prefix. The switch can be combined with `-Api` and `-Web`.
For those who have some weird allergy to Powershell: you can just cat the file and check
the `docker buildx` commands that you need to run.

If using Docker Desktop, you might have to enable `Use containerd for pulling and storing images` 
for it to work.

## Running
For a new self-hosted installation, run the setup assistant from the repository
root with Node.js and OpenSSL installed.

```sh
node setup.mjs
```

It generates `.env`, `appsettings.json` with a first-account bootstrap code,
two OIDC certificates and their password files for the combined Docker deployment.
Follow the [guided deployment instructions](docs/deployment.md#guided-setup-for-a-new-combined-installation)
to set host permissions, configure HTTPS and start Compose.

There's a `docker-compose.yml` file that serves as a starting point.

For a new database, configure `Authentication:BootstrapCode` with a randomly
generated secret of at least 32 characters. Enter it in the invite field on the
registration page. Once a user exists, remove the bootstrap secret from the
deployment. Further registrations require an invitation issued by a signed-in
account.

In production, configure these backend settings through the mounted appsettings
file or the deployment's secret provider. Use double underscores for
environment variable names.

| Setting | Value |
| --- | --- |
| `Authentication:Issuer` | The public HTTPS URL of the backend, for example `https://api.example.org/` |
| `Authentication:FrontendOrigin` | The HTTPS origin of the frontend, which defaults to `https://books.example.org` |
| `Authentication:SigningCertificate:Path` and `:Password` | PKCS#12 signing certificate and its password |
| `Authentication:EncryptionCertificate:Path` and `:Password` | Separate PKCS#12 encryption certificate and its password |
| `Authentication:DataProtectionKeyPath` | A writable, persistent directory, such as `/app/data/keys` in Compose |
| `Authentication:KnownProxies` | An array of IP addresses of trusted reverse proxies, when TLS terminates at a proxy |
| `Authorization:CatalogueManagerUserIds` | Existing numeric user IDs allowed to edit or delete shared catalogue entries. An empty list grants this permission to nobody. |

On the Raspberry Pi, run `sh ./create-oidc-certificates.sh` from the directory
containing `docker-compose.yml`. See [OIDC certificates](docs/certificates.md#oidc-certificates)
for the generated paths and Compose password settings.

Mount both OIDC certificates with their private keys and keep their passwords in
the deployment's secret store. Plan a maintenance window for replacing them.
See [browser and PWA sessions](docs/browser-sessions.md) for session lifetimes
and deployment verification.
Replacing the encryption certificate requires a new Data Protection key
directory and users will have to sign in again. For local development,
use the `Development` environment, which permits the localhost SPA callback and
uses development certificates.
Configure reverse-proxy access logs to omit query strings for the SignalR path
`/api/bibliotheca-hub`, since browser WebSocket connections carry their access
token in the query.

The API reaches Playwright over a dedicated Compose network. The Playwright
port is private to that network.

For Linux ARM64 production without a desktop, Compose builds the browser service
from `docker/playwright`. Chromium runs in headed mode under Xvfb inside that
container, and the API discovers its private WebSocket endpoint. Build this
service with `docker compose build playwright` and deploy the updated API image
together with the Compose configuration. See [browser deployment and checks](docs/wook-provider.md#production-linux-arm64-in-docker-without-a-desktop).

Fetcher diagnostics are disabled by default. To enable them, set
`Fetchers:SaveTempFiles` to `true` and use a dedicated
`Fetchers:TempFilesSaveLocation` outside the web root. The API cleans the
diagnostic HTML and image files after seven days.

ISBN imports return `202 Accepted` and a `Location` pointing to their status.
The authenticated initiating account can use that URL to see the processed
and failed counts. A batch can contain up to 100 books. If there is insufficient
room for the whole batch, the API returns `429`. Use one backend instance per
SQLite database. The worker resumes unfinished imports on startup and may
retry the last unfinished ISBN after a restart.
The latest imports are also available under ISBN File Upload in Settings.

**⚠ I don't recommend that this app is hosted publicly. If you do host it publicly, do so at your own peril. ⚠**

## External edition search

Authenticated clients can call `GET /api/external-editions/search?title=Matilda&author=Roald%20Dahl`.
The response contains `items` and `failedProviders`. A provider error or timeout leaves
the other providers' results available. Editions with the same valid ISBN are merged,
including equivalent ISBN-10 and ISBN-13 values. Each result includes all of its
provider references and a `bookId` when the ISBN already exists in the catalogue.
Editions without a valid ISBN are identified by provider and external edition ID.
Submit the chosen source to `POST /api/external-editions/import` with its
`providerId`, `externalId`, and an optional `shelfId` owned by the caller. The
response is either `200` with the existing `bookId` or `202` with the private
import status URL in `Location`. The completed status includes the new `bookId`.
Retrying the same edition can return its existing catalogue entry or pending
import even during a provider outage.

`GET /api/external-editions/providers` lists registered providers with their ID, name,
state (`active`, `disabled`, or `requiresConfiguration`), and `available` flag.
Availability indicates configuration readiness. Search failures are reported per request.
ISBN imports try Wook first for covers, then Goodreads, Amazon and ISBNdb. Wook
search results are checked against the product page's ISBN before downloading a
cover; up to five candidates are inspected. The product and image are opened in
the same Playwright browser context. Image URLs must initially use HTTPS
on a Wook domain. Wook can block automated browsers, change its pages or omit a
cover; in these cases the remaining providers are tried. Other providers do not
necessarily verify that the returned cover is for the exact edition.

Providers are registered as `IExternalEditionProvider` implementations. Open
Library is registered and starts disabled. To enable it and identify your
installation to Open Library, set these variables:

```dotenv
ExternalEditionSearch__Providers__openLibrary__Enabled=true
ExternalEditionSearch__Providers__openLibrary__ContactEmail=<your contact address>
```

Open Library requests share a limiter per backend instance. Requests with a
contact address are paced below three per second. Requests without an address
are paced at one per second. Search resolves the editions of up to three matching
works and retains editions already loaded if a subsequent work lookup fails.
See [Open Library's API usage guidelines](https://openlibrary.org/developers/api).

Google Books is registered and starts disabled. To enable it, supply your Google
Books API key through the server's deployment secrets:

```dotenv
ExternalEditionSearch__Providers__googleBooks__Enabled=true
ExternalEditionSearch__Providers__googleBooks__CredentialKey=ExternalEditionSecrets:GoogleBooks
ExternalEditionSecrets__GoogleBooks=<key supplied by deployment>
```

Google Books returns up to 40 volumes per search. Missing keys are reported in
provider status. Quota errors appear in the search's failed providers list.
Review Google's [API terms](https://developers.google.com/terms#e-prohibitions-on-content)
and [Books branding rules](https://developers.google.com/books/branding) before enabling
Google Books for the federated workflow.

Hardcover uses a server-side personal access token with catalogue read access.
It is disabled by default and paces outbound queries at one per second per
backend instance. To enable it, configure:

```dotenv
ExternalEditionSearch__Providers__hardcover__Enabled=true
ExternalEditionSearch__Providers__hardcover__CredentialKey=ExternalEditionSecrets:Hardcover
ExternalEditionSecrets__Hardcover=<token supplied by deployment>
```

See [Hardcover's API guide](https://docs.hardcover.app/api/getting-started/) for
token scopes, quota and usage guidance.

Configure providers on the backend under `ExternalEditionSearch:Providers:<id>`.
Providers default to disabled. Lower `Priority` values take precedence for display
metadata and result order. The default priority is 100. `TimeoutSeconds` defaults
to 5 (1–60), and `ResultLimit` defaults to 20 (1–100).
`ExternalEditionSearch:MaxConcurrentRequests` caps simultaneous provider calls
across searches in a backend instance and defaults to 4 (1–32).
Provider implementations must pass the supplied cancellation token through to
upstream requests. The timeout starts when a provider call acquires a slot.

For a registered provider with ID `example`, these environment variables enable it
and point to a separate server-side credential supplied by the deployment:

```dotenv
ExternalEditionSearch__Providers__example__Enabled=true
ExternalEditionSearch__Providers__example__CredentialKey=ExternalEditionSecrets:Example
ExternalEditionSecrets__Example=<credential supplied by deployment>
```

The provider status endpoint exposes neither the credential nor its configuration key.
See [the HTTP examples](src/Bibliotheca.Backend/ExternalEditionSearch.http).
The [provider assessment](docs/external-edition-provider-assessment.md) records
the comparison and each provider's integration status.

## Author photos

Book imports and catalogue refreshes look up missing author photos in Open Library.
Bibliotheca uses an Open Library edition link when the import supplies one. For
other sources, it uses a valid ISBN and verifies that the returned edition
contains the same ISBN and has a matching title. Each author must match exactly
one person linked to that edition by name or a recorded alternate name. An
ambiguous name, missing edition or missing photo leaves the portrait blank in
the database. See the [Editions API](https://openlibrary.org/dev/docs/api/books)
and [Authors API](https://openlibrary.org/dev/docs/api/authors).

Bibliotheca stores the Open Library portrait URL on the author for reuse.
The author page loads that URL from the [Covers API](https://openlibrary.org/dev/docs/api/covers)
and shows a placeholder if the image is unavailable. Photo lookup runs
independently of external edition search settings, shares Open Library's
request limiter, and has a five-second budget per book. A timeout or provider
error leaves the book and its author associations available.

## Tests

Run the backend suite from the repository root with the .NET 11 SDK:

```sh
dotnet test Bibliotheca.slnx
```

Run the frontend suite with a Node.js version supported by `package.json`:

```sh
cd src/Bibliotheca.Frontend
npm ci
npm test
```

The frontend tests run in jsdom with mocked HTTP calls. The backend suite includes
unit tests, controller tests using in-memory SQLite, and HTTP integration tests
using ASP.NET Core TestServer with a temporary database and stub metadata providers.

For line and branch coverage, run `dotnet test Bibliotheca.slnx --collect "XPlat Code Coverage"`
from the repository root, and `npm run test:coverage` from `src/Bibliotheca.Frontend`.
The backend report excludes the EF Core migration classes and model snapshot.

Run the browser tests from `src/Bibliotheca.Frontend`:

```sh
npx playwright install chromium
npm run test:e2e
```

Playwright serves a production build with local API fixtures and synthetic
sessions. The tests exercise service worker updates and offline account changes,
keyboard search, and recovery from a failed ISBN import.

## License

This project is licensed under a *Personal Use License*.  
You may use, copy, modify, and distribute the software **for personal,
non-commercial purposes only**, with mandatory attribution.

**Commercial or enterprise use is strictly prohibited** without prior written permission from the author.

See the full license in [`LICENSE.md`](LICENSE.md).

### Building container images

The API and web Dockerfiles live in `docker/api/Dockerfile` and `docker/web/Dockerfile`.
Both use the repository root as their build context. `build.ps1 -Api -Web -Registry <registry/>`
uses Buildx to build and push a multi-platform manifest for `linux/amd64` and `linux/arm64`;
it requires a builder and a registry that support multi-platform images. The Playwright
image uses `docker/playwright` as its build context and can be built with `-Playwright`.
The Buildx `--push` output sends images directly to the registry rather than loading them locally.

### Monitoring refresh jobs

Use `docker compose logs -f api` to follow catalogue and author refreshes. At Information
level, worker messages identify the batch and item IDs, outcome and duration, including
recovery of an expired worker lease and batch completion. Warnings indicate individual
failures and errors indicate worker-level failures. Configure the corresponding
`Logging:LogLevel` settings to reduce or increase the output. The Settings page retains
the summary and item errors when the log is no longer available.

For a registry that explicitly serves plain HTTP, such as the local Raspberry Pi
registry, use `./build.ps1 -Registry registry.example.org:5000/ -RegistryHttp -Api -Web`.
The script creates or reuses a separate Buildx builder configured for HTTP to that
registry only; it does not modify the current default builder. Use this switch only
on a trusted network: image transfers to this registry are not encrypted. Docker on
the Raspberry Pi must also permit pulling from that HTTP registry. The normal command
without `-RegistryHttp` continues to use the selected builder and its HTTPS settings.

## Self-contained Docker image

The optional `bibliotheca-app` image includes frontend, API, nginx and
Playwright/Chromium with Xvfb. It supports either a configurable external TLS
proxy or direct HTTPS, while the separate images remain available. See
[the deployment guide](docs/deployment.md) for first installation, runtime
configuration, persistence, multi-platform builds and migration.

Book covers are public and cached for one day by default. Set `CoverCache__MaxAgeSeconds` (0 to 31536000) on the API to change the cache duration. Updated covers can remain cached until that duration expires.
