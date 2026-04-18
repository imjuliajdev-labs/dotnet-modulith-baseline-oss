# dotnet-modulith-baseline

An opinionated ASP.NET Core modulith baseline that is built to resist architectural drift.

This baseline gives you a clean host, strict module boundaries, a dispatcher-based application layer, runtime module activation, and executable quality gates from day one. It is meant to be forked, extended, and kept honest as more modules and features are added.

## What this baseline optimizes for

- A composition-only host.
- Clear module ownership for API, application, domain, infrastructure, and published contracts.
- Cross-module communication only through `.PublicContracts`.
- Minimal APIs instead of controllers.
- Consistent use-case execution through `IDispatcher`.
- Runtime module enable and disable without restarting the process.
- Architecture and integration tests that fail when the skeleton drifts.

## What this repo is and isn't

This repo is:

- a governed, production-leaning modulith baseline
- an industrialized foundation for teams that care about long-term drift resistance
- a scaffold-first architecture where new modules extend the platform instead of redefining it
- a teaching repo with live reference modules that demonstrate the supported seams

This repo is not:

- a minimal tutorial or low-ceremony sample
- a generic plug-anything-in template with loose conventions
- a provider-agnostic backend; the supported composed runtime is PostgreSQL-backed

If you want the fuller baseline overview and adoption tradeoffs, read [docs/BASELINE_OVERVIEW.md](docs/BASELINE_OVERVIEW.md). For the governed scope statement that frames what "maintained baseline" means here, see [docs/BASELINE_DECLARATION.md](docs/BASELINE_DECLARATION.md); for a candid critique of the architecture's concentration, consistency, adoption, and maintenance risks, see [docs/ARCHITECTURAL_RISKS.md](docs/ARCHITECTURAL_RISKS.md).

## Included modules

- `Platform`: bootstrap manifest, operational health, and runtime module administration. The `/api/v1/platform/bootstrap` manifest exposes each module's route and metadata namespace as `moduleNamespace`.
- `SampleFeature`: the smallest scaffold-first reference slice for module shape, scheduling, and outbox-backed publication.
- `Blog`: the first production-grade EF-first teaching module with a real aggregate, taxonomy, scheduling, outbox publication, and matching frontend surface.
- `KnowledgeBase`: a richer content and runtime-settings module that uses specialized PostgreSQL stores where EF is not the best fit.
- `Identity`: production-grade ASP.NET Core Identity with cookie authentication and role-based authorization (`Admin`, `User`, `Machine`), recent-auth step-up, explicit security-stamp-driven session revocation, admin-managed user and machine-client lifecycle, 12-character minimum password policy with complexity, lockout-backed sign-in, and time-zone preference support. All Identity endpoints route through the module dispatcher. See [docs/adr/ADR-IDENTITY-POSTURE.md](docs/adr/ADR-IDENTITY-POSTURE.md).
- `Admin`: cross-module projections, machine-consumable reads, recovery workers, and realtime updates.

`SampleFeature` remains the smallest scaffold-first example. `Blog` is the first bounded EF-first reference slice. `KnowledgeBase` is the richer content-flow reference when you need a larger runtime-settings and editorial model.

For a module-by-module explanation of what each checked-in module is for and when to use it as reference material, see [docs/MODULE_GUIDE.md](docs/MODULE_GUIDE.md).

## Solution shape

```text
src/
	ApiHost/
	BuildingBlocks/
		Application/
	Modules/
		Platform/
		SampleFeature/
		Blog/
		KnowledgeBase/
		Identity/
		Admin/
tests/
	Architecture.Tests/
	Integration.Tests/
docs/
	adr/
```

Each module follows the same structure:

- `{Module}.Api`
- `{Module}.Application`
- `{Module}.Domain`
- `{Module}.Infrastructure`
- `{Module}.PublicContracts`

## Core rules

- `ApiHost` is composition only.
- Endpoints call `IDispatcher`; handlers do not call other handlers directly.
- Only `.PublicContracts` may be referenced across modules.
- Persistence belongs only in `.Infrastructure`.
- Expected failures use `Result` and map to `ProblemDetails`.
- Each module exposes exactly one public `IApiModule` entry point.
- Optional modules must be safe to disable at runtime.

The authoritative rules live in [AGENTS.md](AGENTS.md) and the docs under [docs](docs). See the [CHANGELOG](CHANGELOG.md) for version history.

## Quick start

For first-time OSS evaluation, start with the Docker path. The local path is still supported when you want direct repo-based development.

### Recommended: full Docker composed path

This is the best first-time OSS evaluation path because it brings up PostgreSQL, DbMigrator, and ApiHost together with the checked-in Development bootstrap configuration.

```powershell
cp .env.example .env
docker compose up --build
```

Then open `http://localhost:8080`.

Default Development-only bootstrap credentials from `.env.example`:

- browser sign-in: `admin` / `LocalOnly!123`
- machine-auth header: `X-Machine-Key: MachineOnly!123`

Representative endpoints on the full Docker path:

- Host status: `http://localhost:8080/_host/status`
- Bootstrap: `http://localhost:8080/api/v1/platform/bootstrap`
- Antiforgery: `http://localhost:8080/api/v1/identity/antiforgery`

### Alternative: local ApiHost + local frontend/dev path

Use this when you want to run the backend directly from the repo while keeping PostgreSQL on Docker.

```powershell
cp .env.example .env
docker compose up -d postgres

Push-Location web
corepack enable
pnpm install
Pop-Location

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__BaselineDatabase = "Host=127.0.0.1;Port=5432;Database=baseline;Username=postgres;Password=postgres"
$env:Modules__Identity__SeededAdmin__Password = "LocalOnly!123"
$env:ASPNETCORE_URLS = "https://localhost:5079"
pwsh ./scripts/Start-ApiHost.ps1 -Configuration Debug
```

If you want the browser shell on the local path, either:

- run `pnpm dev` from `web` and browse to `https://localhost:3000`, or
- run `pnpm build` once from `web` and let ApiHost serve the compiled `web/dist` assets at `https://localhost:5079`

If you also want the default machine-auth smoke on the local path, set these before starting ApiHost:

```powershell
$env:Modules__Identity__SeededMachine__ApiKey = "MachineOnly!123"
$env:Modules__Identity__SeededMachine__Roles__0 = "Machine"
```

Representative endpoints on the local path:

- OpenAPI: `https://localhost:5079/openapi/v1.json`
- Health: `https://localhost:5079/health`
- Bootstrap: `https://localhost:5079/api/v1/platform/bootstrap`

To run the full local validation wall:

```powershell
pwsh ./scripts/Invoke-LocalGates.ps1
```

`Start-ApiHost.ps1` runs the migrator first and requires `ConnectionStrings__BaselineDatabase`. When `ASPNETCORE_URLS` includes an HTTPS binding, the script exports a temporary local development certificate for Kestrel so the secure browser cookie and antiforgery flow work on the supported local path. Seeded admin and seeded machine bootstrap credentials are Development and Testing only. Any non-Development/non-Testing environment fails fast at startup if they are configured — there is no override flag. The default seeded admin user name is `admin` unless you also configure `Modules__Identity__SeededAdmin__UserName`. Seeded machine credentials carry role assignments (typically `Machine`, optionally `Admin`) drawn from the same `IdentityRole` pool as users; broader automation should move to persisted machine clients through the machine-client administration endpoints. The Identity bootstrap guard treats `ASPNETCORE_ENVIRONMENT=Development|Testing` as a valid local dev/test posture for the migrator path. If one of the default local ports is already in use, override `ASPNETCORE_URLS` for the current shell instead of killing all running `dotnet` processes.

If your shell or browser does not trust the local HTTPS development certificate yet, run `dotnet dev-certs https --trust` once for your machine. For local PowerShell probes only, `-SkipCertificateCheck` is also acceptable.

## Identity posture

The Identity module is a production-grade implementation built on standard ASP.NET Core Identity, cookie authentication, EF Core, and role-based authorization. The three seeded roles are `Admin`, `User`, and `Machine`.

Notable features:

- role-based authorization enforced by the dispatcher through `RoleRequirement`
- recent-auth step-up for sensitive browser mutations via `POST /api/v1/identity/session/step-up`
- admin-triggered session revocation via `POST /api/v1/identity/users/{actorId}/revoke-sessions`, driven by `UserManager.UpdateSecurityStampAsync`
- user self-password change via `POST /api/v1/identity/session/password` (requires recent authentication)
- admin unlock via `POST /api/v1/identity/users/{actorId}/unlock`
- role replacement via `PUT /api/v1/identity/users/{actorId}/roles` with a self-demotion guard that prevents callers removing their own `Admin` role
- password policy: 12+ characters, digit, lowercase, uppercase, and non-alphanumeric required
- lockout after 5 failed sign-in attempts
- cross-instance cookie invalidation through the shared durable Data Protection key store
- persisted machine clients with hashed secrets and full lifecycle (create, list, get, rotate, disable, reactivate, revoke) behind the separate `Machine` scheme (`X-Machine-Key: {clientId}:{secret}`)

Every Identity endpoint routes through the module dispatcher. `MapIdentityApi` is not used. See [docs/adr/ADR-IDENTITY-POSTURE.md](docs/adr/ADR-IDENTITY-POSTURE.md).

## Runtime module activation

The baseline includes live module control out of the box.

- `GET /api/v1/platform/bootstrap`
- `GET /api/v1/platform/modules`
- `POST /api/v1/platform/modules/{key}/enable`
- `POST /api/v1/platform/modules/{key}/disable`

Example read:

```powershell
Invoke-RestMethod https://localhost:5079/api/v1/platform/modules -SkipCertificateCheck
```

The frontend shell exercises the authenticated enable and disable flow. For scripted mutations, authenticate first and include the antiforgery token from `GET /api/v1/identity/antiforgery`.

When an optional module is disabled:

- its endpoints immediately stop serving requests
- its requests return canonical `503` ProblemDetails responses
- the bootstrap manifest reflects the new state
- re-enabling it does not require an application restart

The `platform` module is mandatory and cannot be disabled.

## Operational readiness

The baseline includes production-grade operational capabilities:

- **Structured logging**: Every log line during an HTTP or dispatched request carries `CorrelationId`, `ModuleKey`, `RequestType`, and `ActorId` via `ILogger.BeginScope`.
- **Custom business metrics**: Dispatcher, outbox, and module-state meters via `System.Diagnostics.Metrics`, exported through the OpenTelemetry pipeline.
- **Rate limiting**: Authentication endpoints are rate-limited per IP (login, step-up) and per authenticated user (password change) using ASP.NET Core sliding window policies.
- **Resilience**: Outbox dispatch includes dead-letter thresholds and deterministic failure classification; module-level concurrency bulkheads prevent cross-module resource starvation.
- **Caching**: Module state reads use `IMemoryCache` with 30-second sliding expiration and explicit invalidation on state transitions.
- **Cursor-based pagination**: Public list endpoints (blog posts, knowledge base entries, audit events) use keyset pagination with opaque cursors for consistent performance at any page depth.
- **Outbox dead-letter visibility**: `GET /api/v1/platform/outbox/dead-letters` exposes dead-lettered events for operator diagnosis. No replay — see `docs/adr/ADR-OUTBOX-DEAD-LETTER-VISIBILITY.md`.
- **Form validation**: React Hook Form + Zod with accessible field-level validation (`aria-invalid`, `aria-describedby`) across all frontend forms.
- **Migration rollback**: `DbMigrator --rollback-to <migration-name>` for EF Core modules. See [docs/MIGRATION_ROLLBACK.md](docs/MIGRATION_ROLLBACK.md) for the BP-020 expand-and-contract policy and the decision tree between `--rollback-to`, application rollback, and restore-from-backup; see [docs/operations/rollback/README.md](docs/operations/rollback/README.md) for the step-by-step operator procedure.
- **Deployment reference**: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) documents the required configuration surface (database, Identity bootstrap, OpenTelemetry, ASP.NET Core) for real-environment deployments.
- **Handler test coverage gate**: Architecture test verifying every handler has either a corresponding test class or a governed handler-coverage inventory entry naming the covering integration test. A local/CI-generated report is written to `artifacts/handler-coverage.json`, and CI publishes it from the architecture-tests job.

See [docs/TELEMETRY.md](docs/TELEMETRY.md) for the full observability reference.

## Quality gates

This template ships with executable guardrails.

### Architecture tests

The architecture suite verifies:

- required module project layout
- one public `IApiModule` per `.Api` assembly
- host dependency boundaries
- cross-module reference rules
- dispatcher marker usage
- `DbContext` placement
- handler registration completeness
- minimal API policy

### Integration tests

The integration suite verifies:

- bootstrap output
- OpenAPI exposure
- core-module protection
- no-restart module disable and re-enable behavior

Run both before merging any structural change.

## Adopting this repo as your base

If you want to fork or rename this baseline into a new product repo, start with [docs/ADOPT.md](docs/ADOPT.md).

This baseline has one governed adoption workflow with two supported entry points:

- **Human-first guided adoption** — run `pwsh ./scripts/Start-Adoption.ps1`
- **Advanced config-first adoption** — use `templates/adoption/adopt-spec.example.json` with `pwsh ./scripts/Adopt-Baseline.ps1`

Before you start:

- clone this baseline locally into a **source baseline repo** folder
- choose a separate **target product repo** folder for your adopted project
- run the adoption workflow from the **source baseline repo root**, not from GitHub and not from inside the target folder
- the target folder should ideally **not exist yet**; if it already exists, keep it **empty**

Example:

- source baseline repo: `C:\Users\jcv02\Documents\repos\dotnet-modulith-baseline-oss`
- target product repo: `C:\Users\jcv02\Documents\repos\hspsv2`

If you are a first-time human adopter and have not finalized the target folder, project name or slug, or module-retention choices yet, start with:

```powershell
pwsh ./scripts/Start-Adoption.ps1
```

If you already know those inputs or want the automation or agent path, use:

```powershell
cp ./templates/adoption/adopt-spec.example.json ./adopt-spec.json
pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json -DryRun
pwsh ./scripts/Adopt-Baseline.ps1 -SpecFile ./adopt-spec.json
```

Adoption validation profiles dispatch through the canonical local gate runner: `full` runs `pwsh ./scripts/Invoke-LocalGates.ps1` in manifest order, `fast` runs a reviewed manifest-backed subset, and `none` skips validation.

`Platform` and `Identity` are always required. Use [docs/MODULE_GUIDE.md](docs/MODULE_GUIDE.md) to decide which teaching modules to keep, and use [docs/ADOPT.md](docs/ADOPT.md) for the full adoption guide, rename/removal reference, and validation details. Agent-driven adoption follows the same workflow; the repo-owned helper prompt is [prompts/adopt-governed-baseline.md](prompts/adopt-governed-baseline.md).

## Reference workflow for adding a module

1. Start with [scripts/New-Module.ps1](scripts/New-Module.ps1) and a module spec, not by copying an existing module.
2. If the module is expected to reference another module's `.PublicContracts`, declare those module names explicitly in `crossModulePublicContractDependencies` and run `pwsh ./scripts/Report-ModuleDependencies.ps1` before scaffolding so BP-033 headroom is visible up front.
3. When the module exposes operator-managed settings, declare them in the spec as typed setting definitions with names, primitive types, defaults, and reload-safe metadata.
4. Keep the public surface in `.PublicContracts`.
5. Register handlers from the module's `.Application` assembly.
6. Map endpoints only from the module's `IApiModule` entry point.
7. Extend the scaffolded EF Core persistence shell in `.Infrastructure/Persistence` when the module needs relational data, and replace the generated entity-configuration example before the first real migration.
8. Use [src/Modules/Platform/Platform.Infrastructure/Persistence](src/Modules/Platform/Platform.Infrastructure/Persistence) for the baseline EF wiring and [src/Modules/Blog/README.md](src/Modules/Blog/README.md) for the first production-grade bounded EF module.
9. Treat KnowledgeBase raw SQL as a specialized pattern, not the default persistence baseline.

For the repo-specific persistence guidance, see [docs/EF_MODULE_PERSISTENCE_GUIDE.md](docs/EF_MODULE_PERSISTENCE_GUIDE.md) and [docs/REFERENCE_MODULE_TEACHING_MAP.md](docs/REFERENCE_MODULE_TEACHING_MAP.md).

Detailed guidance is in [docs/ADD_MODULE.md](docs/ADD_MODULE.md), [templates/module/module-spec.example.json](templates/module/module-spec.example.json), and [docs/QUALITY_GATES.md](docs/QUALITY_GATES.md).

## Local runtime requirements

The composed runtime requires PostgreSQL through `ConnectionStrings__BaselineDatabase`. There is no file-backed or SQLite fallback for the shared runtime.

The supported runtime also requires PostgreSQL prepared transactions to be enabled (`max_prepared_transactions > 0`) because command writes can coordinate module state, shared-runtime idempotency, audit, and outbox persistence against the same database in one transactional flow. The checked-in `docker-compose.yml` already starts PostgreSQL with `max_prepared_transactions=64`; if you point the baseline at a different PostgreSQL instance, configure that instance accordingly.

Same-origin is the default browser posture. The checked-in Development profile already allowlists `https://localhost:3000` for the default Vite dev server. If you run the frontend from a different origin, explicitly allowlist that origin through `Frontend:AllowedOrigins` for the backend environment you are using.

- Full composed evaluation path: `docker compose up --build`
- Backend-only local development path: `docker compose up -d postgres`
- Apply migrations only: `pwsh ./scripts/Invoke-DbMigrator.ps1`
- Start the host with migrations: `pwsh ./scripts/Start-ApiHost.ps1`
- For the browser shell on the local path, run `pnpm dev` from `web` or run `pnpm build` once so ApiHost can serve `web/dist`

Docker Compose requires a `.env` file at the repository root. Copy `.env.example` to `.env` before running `docker compose up`. The `.env.example` file documents all required variables with development defaults. For the full secret-source model (Development, CI, Production), including the naming conventions module authors follow for connection strings, module options, and external credentials, see [docs/SECRET_MANAGEMENT.md](docs/SECRET_MANAGEMENT.md).

`web/playwright.config.ts` also respects `ConnectionStrings__BaselineDatabase`, so browser E2E can be pointed at a fresh local database when you want to isolate Playwright from an existing development volume state.

## Maintainer OSS release workflow

If you maintain a private source clone plus a clean public OSS clone, run the release cut from the private repo with:

```powershell
pwsh ./scripts/Release-Oss.ps1 -Version 1.0.1
```

By default the script expects the public clone at `../dotnet-modulith-baseline-oss`. It refuses to release from a dirty private working tree, refreshes the public clone from `origin/main`, replaces that working tree with a tracked-files-only `git archive` snapshot of the current private `HEAD`, and expands that snapshot with the .NET tar APIs so the workflow does not depend on whichever `tar` implementation happens to be first on `PATH`. It then runs `secret-scan` in both repos, runs the selected public validation profile, creates and pushes the private source tag (`private-v1.0.1`), then commits, tags, and pushes the public release (`v1.0.1`). Use `-ValidationProfile none|fast|full` to choose how much validation the public cut runs before push, and use `-Force` only when you intentionally want the script to discard local changes in the public clone. When `full` validation reaches the Testcontainers-backed integration path, the release workflow now attempts to start Docker automatically and waits for `docker info` before continuing; Docker still needs to be installed and reachable on the machine.

## Why use this as a baseline

Most modulith examples prove the happy path once. This one is designed to keep working after multiple teams add modules, persistence, endpoints, and cross-module interactions over time.

Treat the architecture, composition model, runtime controls, Identity implementation, and quality gates as production-grade baseline material. Adopters who need a different identity provider can replace `Identity.Infrastructure` behind the same module boundary, but the baseline ships a working production answer rather than a stub.

If you want a foundation that stays generic, small, and enforceable, this repo is the starting point.
