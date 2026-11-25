# Bibliotheca agent guide

## Repository map

| Path | Responsibility |
|---|---|
| `src/Bibliotheca.Backend/Program.cs` | ASP.NET Core composition, OpenIddict, policies, SignalR and service registration |
| `src/Bibliotheca.Backend/Controllers`, `Services`, `Fetchers` | HTTP contracts, domain operations and integrations for book metadata and covers |
| `src/Bibliotheca.Backend/Data`, `Migrations` | EF Core entities and SQLite schema. Keep new migrations and the model snapshot excluded from coverage, following existing attributes. |
| `src/Bibliotheca.Backend/Jobs`, `Hubs` | Persistent ISBN queue, background processing and import progress sent only to the initiating account |
| `src/Bibliotheca.Frontend/src` | Vue PWA. Pages and components use composables and stores for feature state, with OIDC session handling under `auth`. |
| `tests/Bibliotheca.Backend.Tests` | xUnit tests, temporary SQLite databases and ASP.NET Core TestServer integration tests |
| `src/Bibliotheca.Frontend/tests`, `e2e` | Vitest suites and Playwright browser tests with local fixtures |
| `docker-compose.yml`, `docker/api/Dockerfile`, `docker/web/Dockerfile` | Separate web and API images and supporting services. The root README explains deployment configuration. |

The catalogue is shared. Shelves, reading status, ratings and import history belong to an account. ISBN submissions return a status URL. The persistent worker resumes unfinished imports after a restart. The PWA caches public covers and keeps account-specific data isolated.

## Validate changes

Use the .NET target in the project files and the Node version in `.node-version`. At the time this guide was written, the backend targets .NET 11. See the [README](../README.md) for running the application and setting up local authentication.

| Scope | Command |
|---|---|
| Backend from the root | `dotnet test Bibliotheca.slnx` |
| Backend coverage | `dotnet test Bibliotheca.slnx --collect "XPlat Code Coverage"` |
| Frontend from `src/Bibliotheca.Frontend` | `npm ci` if dependencies are missing, then `npm test` |
| Frontend coverage | `npm run test:coverage` |
| PWA and browser behaviour | `npx playwright install chromium` if needed, then `npm run test:e2e` |

Choose checks based on the affected layers and the issue's acceptance criteria. For external providers, use fixtures in automated tests and report separately what was verified with a live credential. Keep provider credentials in local or deployment secret configuration.

## Working from GitHub

Use `$bibliotheca-issues` to list open issues or retrieve an issue by number. The skill reads the live backlog with authenticated GitHub CLI (`gh`) against `labsilva/bibliotheca`, including comments and sub-issues where relevant.

Use `$bibliotheca-deliver` with the selected issue number to implement and validate the change. An authorized delivery also updates the applicable GitHub Projects status, posts a report and closes the issue once the work is published and the acceptance criteria are met. State the scope of publication and GitHub updates in the request.

## Official .NET skills

The portable skill directories in `.agents/skills` come from [dotnet/skills at revision c227688](https://github.com/dotnet/skills/tree/c227688ed14de4467e3e45f95dc69a94e905e219). Each folder retains the upstream files. The upstream [MIT licence](dotnet-skills-LICENSE.txt) applies to those skills.

The import covers core .NET and C#, advanced .NET, ASP.NET Core Web API, data access, diagnostics, MSBuild, NuGet, testing, test migration, framework upgrades and .NET 11 APIs. Use only the skills relevant to the issue. For example, start with `dotnet-webapi` for a new API, `optimizing-ef-core-queries` for query performance, or `run-tests` and `coverage-analysis` for validation. Inspect and resolve any upstream cross-skill references as needed. This import covers portable skills, with no additional host agents or workflow integrations.
