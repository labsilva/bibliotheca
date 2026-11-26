# Docker deployment

Bibliotheca supports two deployment models. `docker-compose.yml` keeps API, web
and Playwright in separate images. `docker-compose.app.yml` runs all three in
one `bibliotheca-app` image: nginx serves the frontend, Kestrel serves the API,
and Chromium runs through Playwright under Xvfb. Both images support Linux
amd64 and arm64; no desktop environment is needed on the host.

## Guided setup for a new combined installation

From the repository root, run `node setup.mjs` with Node.js and OpenSSL installed.
The assistant asks for your HTTPS origin, TLS mode, host networking and image.
It writes `.env` and `appsettings.json`, creates the mount directories, generates
two OIDC certificates and stores their passwords in separate files under
`secrets/`. It refuses to overwrite existing configuration or initialize a
populated data directory. No npm installation is required.

Before starting Compose on Linux, apply the bind-mount permissions described
under [Process identities](#process-identities-and-chromium-isolation) and
[Mounted certificate passwords](#mounted-certificate-passwords). Configure your
reverse proxy or supply the direct TLS PEM files. Then follow the printed build
or pull commands. Read the bootstrap code from the protected `appsettings.json`
to register the first account, remove the code, assign the administrator ID and
restart the app as described below. Use a published release tag if you choose
to pull a prebuilt image.

## Combined image: first installation

1. Copy `docker/app/env.example` to `.env` and edit it. Set `PUBLIC_URL` to the
   actual HTTPS origin, without a path (for example `https://books.example.org`).
   No particular hostname, registry, reverse proxy, or Docker network is required.
2. Copy `docker/app/appsettings.example.json` to `appsettings.json`. Create the
   data, temp and cert directories selected by `.env` before starting Compose.
   For a new database, configure `Authentication:BootstrapCode` with a random
   secret of at least 32 characters, kept outside version control. The first
   registration requires that secret; remove it after creating the account.
   Configure `Authorization:CatalogueManagerUserIds` for the accounts allowed
   to manage the shared catalogue; see the root README for details.
   After registration, determine the administrator's numeric ID from the
   `Users` table in the SQLite database and configure
   `Authorization:AdministratorUserIds` with that ID, then restart the app.
   Only configured administrators may create invitations; an empty list
   grants nobody that permission. Do not assume an existing catalogue manager
   or the first account is automatically an administrator.
3. Create **two distinct** PKCS#12 certificates for OIDC signing and encryption,
   as described in the root README. Put them at `CERT_PATH/signing.pfx` and
   `CERT_PATH/encryption.pfx`, and store each password in a separate file under `secrets/`. Set
   `OIDC_SIGNING_PASSWORD_FILE` and `OIDC_ENCRYPTION_PASSWORD_FILE` in `.env`
   to those host paths; Compose mounts their contents under `/run/secrets`. These are not
   the web server's TLS certificate. They and the persistent Data Protection
   keys in `/app/data/keys` must survive upgrades and restarts.
4. Choose TLS proxy or direct TLS below. Build locally with
   `docker compose -f docker-compose.app.yml build`, or set `APP_IMAGE` to a
   published image and run `docker compose -f docker-compose.app.yml pull`.
5. Start with `docker compose -f docker-compose.app.yml up -d`. Open `PUBLIC_URL`
   and register the first account. Inspect logs with
   `docker compose -f docker-compose.app.yml logs -f app`.

The sample publishes host ports on loopback by default. Change
`APP_BIND_ADDRESS` deliberately when another machine must reach the container.
Compose variables also configure image, port mappings, mount locations, upload
limit and shared memory. Additional backend settings use the normal
`appsettings.json` keys or ASP.NET `Section__Key` environment overrides in a
Compose override. Combined mode owns its issuer/frontend origin, loopback
Kestrel endpoint and internal Playwright address; derive these from `PUBLIC_URL`
instead of configuring conflicting values.

The built-in nginx access log omits query strings. Configure every external
reverse proxy, including Nginx Proxy Manager, and any telemetry collector to
omit or redact query strings and authorization headers as well: SignalR and
invitation links can contain credentials. Review existing logs and their
retention before exposing an installation publicly.

## External TLS proxy (nginx, Nginx Proxy Manager, Traefik, Caddy, etc.)

Use `TLS_MODE=proxy`. Forward to the app's HTTP port and configure the outer
proxy to preserve `Host`, set `X-Forwarded-Proto: https`, and correctly set
`X-Forwarded-For`; enable WebSockets and match any upload/time limits needed.
Set `TRUSTED_PROXIES` to comma-separated **connecting proxy** IPs or dedicated
CIDR networks. The app rejects requests without HTTPS metadata from a trusted
source; broad trust ranges allow other clients to forge proxy metadata.

For a proxy on the host, point it at `127.0.0.1:APP_HTTP_PORT` and identify the
actual Docker-side source address seen by nginx. For a proxy in another
container, attach both containers to a shared network with a Compose override
and forward to `app:80`. Prefer trusting a fixed proxy address or a dedicated
proxy network. Container names are not accepted in `TRUSTED_PROXIES`.

For **Nginx Proxy Manager**, create a Proxy Host for the `PUBLIC_URL` hostname,
select scheme HTTP and the reachable app address/port, enable Websockets
Support, and configure its TLS certificate and Force SSL. Configure trust as
above using your own network/IPs; the image has no NPM-specific dependency.

The inner nginx normalizes headers and Kestrel accepts its local forwarding.
Kestrel and Playwright listen only on loopback; never publish ports 5500, 8080
or 8081 from inside the combined container. Only nginx's ports 80/443 are public.

## Direct HTTPS without an external proxy

Set `TLS_MODE=direct`. Supply a valid PEM certificate chain and unencrypted PEM
private key using `TLS_CERTIFICATE` and `TLS_CERTIFICATE_KEY` paths inside the
container, mounted read-only (the example uses `CERT_PATH/tls`). Set
`APP_BIND_ADDRESS` and published HTTPS port as needed, and include a nonstandard
public port in `PUBLIC_URL` if applicable. HTTP redirects to that configured
HTTPS origin. `TRUSTED_PROXIES` is unnecessary; forwarded metadata from clients
is ignored. Use a certificate trusted by browsers; the image does not request
public certificates or automatically renew them. Restart/recreate the container
after replacing certificates.

## Build and lifecycle

`./build.ps1 -Registry your-registry/ -App` builds and pushes a multi-platform
manifest for linux/amd64 and linux/arm64. Add `-RegistryHttp` only for an
explicitly HTTP registry. `-Api`, `-Web` and `-Playwright` continue building the
separate images. Playwright client, server and browser image versions must be
updated together.

The build runs package updates in the target image. Use a Buildx builder with
native nodes for both target architectures or configured ARM64 emulation when
building on amd64. For a security rebuild, refresh the base images and bypass
cached package-install layers so the build applies current repository updates.

The combined entrypoint validates configuration and nginx before starting the
API, nginx and Playwright/Xvfb. A service exit terminates the container, and
Compose's restart policy restarts it. SIGTERM stops all process groups. Logs
go to Docker stdout/stderr. The healthcheck probes the browser and an API
database-readiness request through nginx; initialization/migrations complete
before Kestrel begins accepting requests. Chromium needs shared memory; the
example reserves 512 MB (`APP_SHM_SIZE`). Measure host RAM with your workload.

## Existing data and separate mode

Back up SQLite and retain certificates and Data Protection keys. Reuse the
existing data/cert mounts, point `PUBLIC_URL` at the combined HTTPS origin, and
update the outer proxy. Changing the OIDC issuer may require users to sign in
again. Do not run two API instances against the same SQLite file concurrently.
The separate Compose file remains independent and retains external Playwright.

The web image now accepts runtime `OIDC_AUTHORITY`, `OIDC_CLIENT_ID`,
`OIDC_REDIRECT_URI`, `OIDC_POST_LOGOUT_REDIRECT_URI`, `OIDC_SCOPE` and `API_URL`.
Legacy `VUE_APP_VITE_OIDC_*` aliases remain accepted. Runtime values are public,
never secrets. Vite environment settings remain development/build fallbacks.
The combined image derives those values from `PUBLIC_URL` and uses the existing
`bibliotheca-web` public client. Subpath hosting is not supported by this model.

## Verification before distributing a release

Run backend and frontend suites, then build and run both Compose variants.
Check SPA deep links; OIDC discovery/JWKS with public HTTPS URLs; initial
registration, login/logout and refresh; protected API 401/403; SignalR
WebSocket upgrade; covers; author/book extraction through Playwright; SQLite
and login-key persistence after recreation; API/browser failure health and
SIGTERM shutdown. In proxy mode verify untrusted/spoofed forwarding is rejected.
In direct mode verify HTTPS and HTTP redirects with the supplied certificate.
Repeat the smoke checks on native ARM64 as well as amd64. A successful manifest
build alone does not demonstrate runtime behaviour on ARM64.

## Process identities and Chromium isolation

The API runs as `app` (UID/GID 1654), including in the combined image. On a Linux
Docker host, prepare bind mounts for that identity before starting:

```sh
sudo install -d -m 700 -o 1654 -g 1654 data temp cert
sudo chown 1654:1654 appsettings.json cert/*.pfx
sudo chmod 600 appsettings.json cert/*.pfx
```

For direct TLS use `sudo chmod 750 cert`, create `cert/tls` owned by
`1654:1654` with mode 750, and set its PEM files to owner/group `1654:1654`
and mode 640. The combined supervisor receives supplemental group 1654 through
Compose, allowing nginx to traverse these directories and read TLS PEM files
without `DAC_OVERRIDE`. Keep OIDC PFX files and appsettings mode 600. In proxy
mode the cert directory may remain 700. The separate `pwuser` browser
must not read the directory, API configuration or data. Combined startup refuses
readable secret mounts. Chromium receives a clean environment without API
passwords/bootstrap codes. Do not mount secrets into the browser service.
Docker Desktop bind permissions differ from native Linux; use a Linux filesystem
or provision named volumes with these ownership/mode settings when required.
The API cannot repair root-owned mounts automatically.

Production rejects a SQLite database or fixed file log configured inside the
backend web root. Point the connection string at the existing private database
outside that root; the application does not move data automatically. SQLite
`file:` URI connection strings are not supported in production. Path validation
is lexical and does not resolve symlinks: do not use a symlink that makes a
private storage path reachable from the web root. Keep dynamic log destinations
in a dedicated private directory.

Chromium runs with its sandbox enabled. Compose uses the upstream Playwright
seccomp profile in `docker/playwright/seccomp.json`, covering amd64 and arm64 and
allowing the user namespace operations needed for the sandbox. It is sourced from
[Microsoft Playwright](https://github.com/microsoft/playwright/blob/main/utils/docker/seccomp_profile.json).
The profile additionally permits `chroot` unconditionally at the syscall layer:
Chromium needs it inside its user namespace when container capabilities are
dropped. Kernel capability checks still apply; no host `SYS_CHROOT` capability
is granted to the browser.
Keep the profile reviewed with browser updates. The host must permit unprivileged
user namespaces (including its AppArmor policy); a denied namespace causes startup
failure. Do not fix it by enabling privileged mode or disabling Chromium's sandbox.
Capabilities are dropped; the combined root supervisor retains only identity
switching, process termination and low-port binding capabilities.

Browser traffic uses an HTTPS CONNECT proxy which denies IP literals, non-443
ports, private/loopback/link-local/metadata destinations and IPv6. DNS is resolved
and validated before connecting to the exact IPv4 address, preventing rebinding
between validation and connection. HTTP-only and IPv6-only sources will fail.
Chromium's implicit loopback proxy bypass, QUIC and non-proxied WebRTC UDP are
disabled. This deliberately narrows browsing functionality and must be checked
with Wook after browser/provider changes.

In separate mode the browser has only an internal Docker network; the egress
service is the public-network bridge and enforces this proxy policy. No browser
ports are published. The API is on a different internal control network. The
`browser-control` service relays only TCP ports 8080 and 8081 to the browser;
it accepts no destination supplied by its peer. It and the egress service have
kernel IPv4 forwarding disabled and no network administration capabilities.
The browser therefore has no direct API network attachment. This reduces lateral
reach after browser process compromise, but does not protect against a container
or kernel escape, exploitation of the relay/proxy, or malicious replies over the
existing API control connection. Keep Docker's inter-network isolation enabled.
The browser filesystem is read-only with bounded disposable home and temporary
directories; executable scripts are root-owned. The CONNECT proxy permits at
most 32 active or resolving tunnels and times out pending/idle connections.
The combined image also shares loopback, process namespace and a root supervisor;
its proxy is a navigation defence, not an OS containment boundary after exploitation.
For hostile browsing, prefer separate containers on a dedicated browser host/VM,
with a host firewall allowing API-to-browser control connections, denying
browser-to-API/new private-network connections, and allowing browser egress only
through the proxy. Do not attach unrelated services to the browser network.
Validate host-specific firewall rules before claiming post-exploit containment.

On 2026-11-23 the separate topology passed Docker Desktop Linux/amd64 checks:
the API-side probe reached discovery and the Playwright WebSocket through the
fixed relay; a raw process in the browser container could not connect to the
API-side probe by hostname or IP, or directly to the public Internet. Chromium
reported namespace/PID/seccomp sandboxing, two independent fixture sessions
rendered successfully, and three Wook searches plus product pages returned
HTTP 200 through egress. These checks do not simulate a kernel/container escape.
Native ARM64 runtime validation has not been performed.

## Separate-mode public proxy and operations

Copy `docker/separate/appsettings.example.json` to the mounted `appsettings.json`;
replace example origins, exact API `AllowedHosts`, proxy IPs and certificate
passwords. Configure a random `Authentication:BootstrapCode` only for the first
registration, and remove it afterwards. Set `API_PUBLIC_URL` and `WEB_PUBLIC_URL`
in Compose's `.env`; these values are public frontend configuration. The example
backend configuration replaces the local development HTTP/HTTPS listeners with
one container HTTP listener. The API and web host port mappings bind loopback
by default. An external proxy must terminate HTTPS for both public origins,
normalize Host/forwarded headers and trust only its actual connecting address
in `Authentication:KnownProxies`. Use firewall rules when publishing beyond
loopback. Nginx Proxy Manager may create one Proxy Host for each origin, enable
Force SSL and Websockets, and forward to the respective host port. Keep its
administration port private.

The combined public nginx rejects health/OpenAPI/Scalar paths and exposes database
readiness on its private loopback listener only. It sets HSTS, nosniff, no-referrer
and frame denial on public HTTPS responses. In separate mode set those same
headers at each external TLS virtual host, and deny `/api/health`, `/openapi`
and `/scalar` publicly. HSTS belongs on browser-facing HTTPS responses; enable
subdomains/preload only if every affected service supports permanent HTTPS.
Never forward untrusted client-supplied forwarding headers unchanged.

Before upgrades, stop the API and back up the complete data directory (SQLite,
including any WAL/SHM companions, and Data Protection keys), both OIDC certificates,
configuration and secrets to encrypted offline storage. Record the previous image
digest and schema version. Never copy a live SQLite database without a consistent
SQLite backup procedure. Pin release image digests and keep browser client/server
versions aligned. Restore into a separate private installation, with the original
certificate keys and issuer, and verify login and catalogue data before switching
traffic. Migration rollback may require restoring the matching database backup;
keep the production instance stopped while restoring.

Automated configuration/proxy checks: `node --test docker/app/configure.test.mjs
docker/playwright/egress.test.mjs docker/playwright/control.test.mjs`. Container smoke checks remain release gates:
clean startup with restrictive mount permissions, `id` for API and browser,
sandbox-enabled renderer, rejection of private IP/DNS/redirect destinations,
no public discovery/browser/readiness/docs access, Wook extraction, account
bootstrap/login/authorization, TLS trust/host spoofing, persistence and shutdown.
Run both topologies on native amd64 and arm64. These runtime checks require a
working Docker daemon and real certificates; passing Node tests alone does not
validate them.

## JavaScript extractors

See [JavaScript extractors](javascript-extractors.md) for the Bertrand pilot,
including the bind mount, approved origins, and update and rollback steps.

## Mounted certificate passwords

The combined Compose example uses file-backed Compose secrets, keeping password
values out of container environment configuration and `docker compose config`.
Create `secrets/` outside published folders; give the two password files owner
`1654:1654` and mode `600` on Linux, with their host directory mode `700`.
Compose file secrets use bind mounts locally: `uid`, `gid` and `mode` declarations
would not repair host permissions. The API user must read them and the browser
user must not. Combined startup rejects browser-readable password files. Docker
Desktop needs equivalent protected Linux storage; verify permissions in-container.
Use `Authentication:SigningCertificate:PasswordFile` and the matching encryption
setting for separate-mode secret mounts. Do not configure `Password` alongside
`PasswordFile`. When upgrading an existing installation, first copy the existing
password values into the protected files, then remove both certificate `Password`
properties from the mounted `appsettings.json` and any environment overrides.
Remove the old `OIDC_SIGNING_PASSWORD`/`OIDC_ENCRYPTION_PASSWORD` entries from
`.env` after setting the new file paths. Keep the existing certificates and their
passwords unchanged during this configuration migration. An existing `Password`
combined with `PasswordFile` deliberately fails startup instead of silently
choosing one source. One trailing LF or CRLF is removed; other whitespace is preserved.
These local mounts are not an encrypted secret store. Restrict host access and
include secret files in encrypted offline backups. Never put provider credentials,
bootstrap codes or certificate passwords in frontend environment variables.

## Images from GitHub Releases

CI runs on pull requests and pushes to master. Build and tests are blocking,
as are high and critical advisories in npm and NuGet. Semgrep reports matches
in the CI job without failing it for findings. The separate CodeQL workflow runs only when the repository is public.

Publishing a GitHub Release starts a separate workflow. After CI validates the
release commit, it publishes the app, API, web and Playwright images for
linux/amd64 and linux/arm64 under ghcr.io/labsilva/bibliotheca. Each image uses
the release tag. Check the Actions run before installing the release. Use an
explicit release tag when pulling images.

For the separate Compose topology, set these values in the deployment .env,
substituting the release tag you want to install.

```dotenv
BIBLIOTHECA_IMAGE_TAG=v1.2.3
BIBLIOTHECA_PLAYWRIGHT_IMAGE=ghcr.io/labsilva/bibliotheca/bibliotheca-playwright:v1.2.3
```

Authenticate Docker to ghcr.io if the images are private, then run
`docker compose pull` and `docker compose up -d --no-build`. For the combined
topology, use `ghcr.io/labsilva/bibliotheca/bibliotheca-app:v1.2.3`.
