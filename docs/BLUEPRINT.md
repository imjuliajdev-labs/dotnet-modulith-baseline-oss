# Blueprint

This is the industrialized end-state target for this production-grade modular monolith baseline.

The goal is not to stay generic. The goal is to remove architectural ambiguity before the second real module arrives, so new features extend the shape instead of rewriting it.

The target architecture does not include convenience-only bootstrap compromises. Development aids may exist in scripts, local configuration, or other development-only workflow surfaces, but they do not relax the governed end-state described here.

## Document Set

Treat the following files as one governed documentation set:

- `BLUEPRINT.md` defines the target architecture and the non-negotiables.
- `QUALITY_GATES.md` defines how the architecture is enforced and how temporary exceptions are governed.
- `ADD_MODULE.md` defines the only supported workflow for adding a new module.
- `RULE_TO_GATE_CATALOG.md` maps every non-negotiable to its primary enforcement artifact.
- `docs/adr/*` records foundational default changes.
- `README.md` documents the local bootstrap, scripts, and workflow surface.
- `AGENTS.md` is the agent and new-contributor entry point into the governed set.

If any file in the governed set changes, every other file it affects changes in the same review. In particular:

- A change to a non-negotiable in `BLUEPRINT.md` updates `QUALITY_GATES.md` and `RULE_TO_GATE_CATALOG.md` in the same review, and adds or updates an ADR if the default shape changed.
- A change to an enforcement path updates `RULE_TO_GATE_CATALOG.md` (and `BLUEPRINT.md` if the rule wording moved).
- A change to the module-add workflow updates `ADD_MODULE.md`, the scaffold gate in `QUALITY_GATES.md`, and the catalog.
- A new non-negotiable lands with a new rule ID in the catalog and a mapped enforcing artifact in the same review.
- A change to local workflow (bootstrap, scripts, frontend) updates `README.md`.
- A change to agent-facing conventions updates `AGENTS.md`.

This rule is enforced by catalog entry BP-026. A PR that modifies a governed-set file without the matching updates is incomplete.

## Target Outcome

Build a server-first modular monolith with:

- .NET 10 backend
- PostgreSQL as the primary relational store
- a custom in-house dispatcher for CQRS and cross-cutting request orchestration
- ASP.NET Core minimal APIs only
- cookie-based authentication using ASP.NET Core Identity primitives, not JWT browser auth
- React plus Vite frontend with TypeScript in strict mode
- Redux Toolkit plus RTK Query used consistently
- runtime module activation through a Platform module without process restart
- durable idempotency, role-based authorization, and configuration models that survive retries, revocation, and production change
- shared real-time push and module-aware frontend code splitting for optional and enabled modules
- timezone- and DST-safe business behavior by design
- executable architecture, contract, and drift gates that stop regressions before merge

The shared dispatcher is intentionally custom and keeps hot-path invocation off `MethodInfo.Invoke` by using cached compiled invokers. Source-generated dispatch remains an optional future optimization rather than a standing architecture requirement on its own.

## Design Priorities

- Drift resistance over convenience
- One source of truth per concern
- Thin host and thin API edges
- Clear module ownership and stable cross-module contracts
- Identical semantics across HTTP, background workers, startup flows, and internal orchestration
- Best-practice defaults up front, not structural retrofits later
- Best-practice fixes by default; shortcuts are explicit dated waivers, never hidden shims

## Non-Negotiables

- ApiHost is composition only.
- Every use case goes through the dispatcher.
- No module references another module outside that module's PublicContracts.
- PublicContracts contain only versioned cross-module contracts, integration events, public identifiers, and public read models. They do not contain browser transport DTOs or module-internal request contracts. Every integration event carries lifecycle metadata. Every query namespace includes at least one governing service interface, and every non-interface type in that namespace is reachable from a service interface signature or composes reachable types.
- Shared BuildingBlocks projects are tightly governed allowlists, not convenience dumping grounds; new approved shared public surface is admitted only when it is technical or universal and removes more complexity than it introduces.
- Persistence, migrations, startup initialization, outbox processing, and background workers live only in module Infrastructure.
- Runtime module enable and disable is first-class, reversible, cluster-safe, and safe under load.
- Expected failures return Result and map through one shared ProblemDetails contract.
- Unexpected failures are translated centrally through one shared exception-to-error policy.
- The frontend consumes generated API contracts and uses RTK Query as the only remote-data path.
- Cross-module synchronous reads are explicit, bounded, read-only contracts with declared freshness and failure semantics.
- Long-running cross-module workflows use persisted process managers, not ad hoc chains of handlers or events.
- Opted-in command idempotency is durable and returns a stable outcome across retries.
- Authorization is role-based on top of ASP.NET Core Identity; session invalidation uses the standard security stamp; sensitive browser mutations require recent-auth or equivalent step-up protection.
- Module configuration is namespaced, typed, validated, and audited when changed at runtime.
- Inter-module bulkheads prevent one module's saturation from starving unrelated modules.
- Shared real-time browser push and module-aware feature loading use shared infrastructure, not feature-local ad hoc clients.
- Module-to-module contract compatibility is verified for shared query contracts and integration events.
- If machine-to-machine access exists, it uses a separate auth scheme and role assignments, never the browser cookie path.
- Public HTTP and integration contracts are additive-first by default; breaking changes require a new version or an explicitly governed compatibility window.
- Integration event delivery is treated as at-least-once; consumers are replay-safe and use inbox or deduplication when side effects are not naturally idempotent.
- Database evolution follows expand-and-contract rules; destructive schema changes ship only after a compatibility window.
- The baseline includes a deterministic scaffold and a versioned rule-to-gate catalog so structure and enforcement stay in sync.
- Every structural rule that matters maps to an executable gate or a dated waiver entry with an owner and expiry.
- Same-origin browser access is the default; CORS stays disabled unless an ADR-backed allowlist explicitly names trusted origins. When CORS is enabled, origins, methods, headers and exposed headers are all **explicit, wildcard-free allow-lists** bound via `FrontendCorsOptions` and `ValidateOnStart`-guarded — any `*` entry aborts boot. See `docs/adr/ADR-CORS-CONFIGURATION.md`.
- Shared Data Protection keys are persisted durably for multi-instance deployments.
- Authentication endpoints are rate-limited per IP for login and step-up attempts, and per authenticated user for password mutations.
- Cross-module `*.PublicContracts` references are capped at 3 per module (BP-033) to prevent any single module from accumulating unbounded cross-module coupling; current headroom is reported by `scripts/Report-ModuleDependencies.ps1`.
- Shared read adapters declare explicit timeout, freshness, and fallback policies; adapters using fallback behavior implement a catch path.
- Query and reader paths in module Infrastructure prefer projections over eager loading to avoid loading full entity graphs.
- The frontend wraps each feature route in a feature-level error boundary so a runtime failure in one feature does not crash the entire shell.
- Every command handler, query handler, and integration-event handler has either a corresponding test class or a governed handler-coverage inventory entry that names the covering integration test class or classes and justification.
- Public list endpoints use keyset (cursor) pagination; query performance is independent of page depth.
- No commercial packages.
- The governed documentation set stays internally consistent; a change to one file updates all affected files in the same review.
- The supported composed runtime requires configured shared-runtime durable storage; missing `ConnectionStrings:BaselineDatabase` is a startup failure, not a silent in-memory fallback.
- Postgres connection composition is centralized behind `IPostgresDataSourceResolver`; raw `new NpgsqlConnection(...)` construction in `src/**/*.cs` is not a supported pattern. See `docs/adr/ADR-POSTGRES-DATA-SOURCE-RESOLVER.md`.
- Every frontend `<form>` component under `web/src/**` is driven by React Hook Form with a `zodResolver` schema, and every input bound to a validated field renders `aria-invalid` and `aria-describedby` so assistive technology announces validation state. A disabled submit button is never the sole validation UI.
- Every commit reaching `main` is scanned for leaked secrets; a working-tree or git-history match against the pinned secret-detection rule set fails the PR before build.

## Technology Baseline

### Backend

- ASP.NET Core 10
- EF Core 10 for the scaffolded and default relational module persistence path
- Npgsql EF Core provider on that default path; specialized modules may use direct PostgreSQL access when EF is not the best fit
- NodaTime plus Npgsql NodaTime integration
- ASP.NET Core Identity with cookie authentication
- ASP.NET Core Antiforgery
- ASP.NET Core Rate Limiting
- Scalar for interactive OpenAPI documentation
- ASP.NET Core SignalR
- OpenTelemetry for tracing and metrics
- built-in logging or Serilog, but only through shared logging abstractions

### Frontend

- React 19
- Vite
- TypeScript with full strictness enabled
- Redux Toolkit
- RTK Query
- React Hook Form with Zod schema validation
- React Router
- Vitest
- Playwright
- ESLint with import-boundary rules

### Tooling

- Directory.Packages.props for central NuGet version management
- pnpm for frontend package management
- .editorconfig for both C# and TypeScript conventions
- GitHub Actions CI with required checks
- OpenAPI generation plus TypeScript client generation as part of the contract gate
- contract snapshot tooling for HTTP and integration contracts
- a deterministic module scaffold with regression smoke coverage
- PostgreSQL-backed tests via Testcontainers

## Deployment Shape

Use one deployable backend process and one frontend artifact.

- In development, Vite runs separately and proxies to the backend.
- In production, the preferred model is same-origin hosting: the backend serves the built frontend assets.
- All browser API calls are relative-path same-origin calls.
- Authentication uses secure HTTP-only cookies.
- No JWT access tokens in localStorage or sessionStorage.

## Solution Structure

```text
src/
  ApiHost/
  BuildingBlocks/
    Application/
    Domain/
    Infrastructure/
    Testing/
  Modules/
    Platform/
      Platform.Api/
      Platform.Application/
      Platform.Domain/
      Platform.Infrastructure/
      Platform.PublicContracts/
    Identity/
      Identity.Api/
      Identity.Application/
      Identity.Domain/
      Identity.Infrastructure/
      Identity.PublicContracts/
    Admin/
      Admin.Api/
      Admin.Application/
      Admin.Domain/
      Admin.Infrastructure/
      Admin.PublicContracts/
    SampleFeature/
      SampleFeature.Api/
      SampleFeature.Application/
      SampleFeature.Domain/
      SampleFeature.Infrastructure/
      SampleFeature.PublicContracts/
    Blog/
      Blog.Api/
      Blog.Application/
      Blog.Domain/
      Blog.Infrastructure/
      Blog.PublicContracts/
    KnowledgeBase/
      KnowledgeBase.Api/
      KnowledgeBase.Application/
      KnowledgeBase.Domain/
      KnowledgeBase.Infrastructure/
      KnowledgeBase.PublicContracts/
  Tools/
    DbMigrator/
web/
  src/
    app/
    features/
    shared/
  tests/
    unit/
    e2e/
tests/
  Architecture.Tests/
  Integration.Tests/
  Module.UnitTests/
docs/
  adr/
```

## Shared Layer Governance

Shared layers are allowed only if they are explicitly constrained.

### BuildingBlocks.Application

Allowed:

- dispatcher contracts and pipeline abstractions
- Result and Error primitives
- module runtime abstractions
- actor, authorization, and audit abstractions
- ProblemDetails mapping abstractions

Forbidden:

- module-specific DTOs
- business enums or policies owned by a module
- repositories, DbContexts, migrations, and persistence mappings

### BuildingBlocks.Domain

Allowed:

- base entity and value-object primitives
- shared time abstractions such as `IClock`
- integration-event marker abstraction

Forbidden:

- business rules shared "for convenience"
- module-specific concepts, validators, or query models
- EF Core attributes or persistence mappings

### BuildingBlocks.Infrastructure

Allowed:

- host-wide technical adapters used by multiple modules
- shared ProblemDetails writer implementation
- OpenTelemetry and logging adapter wiring
- OpenAPI and generated-client tooling hooks
- migration locking and database bootstrap helpers that are not module-specific

Forbidden:

- module DbContexts, repositories, or SQL
- module-owned outbox implementations that bypass module Infrastructure
- feature behavior or module orchestration

### BuildingBlocks.Testing

Allowed:

- shared test fixtures
- Testcontainers setup
- deterministic time and timezone fixtures
- generated-client smoke harnesses
- common assertion helpers

Forbidden:

- module-specific test behavior that belongs in that module's test project

### Shared abstraction admission rule

No new approved shared public surface is added to `src/BuildingBlocks/*` unless all of the following are true:

- it is clearly technical or universal rather than module-specific business behavior
- it collapses duplicate paths into one canonical mechanism instead of creating a second convenience path
- it removes more complexity than it introduces in shared surface area, ownership, and long-term maintenance
- its owning shared layer is obvious from the current allowlists
- the approved shared public-surface inventory and architecture tests are updated in the same review

If a type exists because two modules happen to need the same business thing today, it stays out of BuildingBlocks. Prefer module-local duplication over widening `BuildingBlocks` prematurely.

## Repository-Owned Platform Subsystems

The baseline owns a small set of explicit platform subsystems directly. They are not incidental helper code and they are not extension points to be bypassed with parallel convenience paths.

### Dispatcher subsystem

Owner: baseline maintainers.

Canonical entry points:

- `BuildingBlocks.Application.Dispatching.DispatcherServiceCollectionExtensions.AddDispatcher(...)`
- `IDispatcher`
- `IIntegrationEventDispatcher`

Invariants:

- request dispatch and in-process integration-event dispatch remain baseline-owned
- built-in pipeline behavior order is explicit and tested
- handler invocation stays off `MethodInfo.Invoke` on the hot path
- discovery and startup validation remain part of the subsystem rather than feature-local DI conventions

Regression expectations:

- architecture tests guard canonical registration and built-in behavior order
- unit and integration tests cover dispatcher behavior, exception translation, idempotency, and telemetry

### Module runtime subsystem

Owner: baseline maintainers.

Canonical entry points:

- `IModuleStateReader`
- `IModuleStateGuard`
- `IModuleExecutionGate`
- `IModuleWorkLeaseManager`
- `ModulePollingBackgroundService`

Invariants:

- durable module state remains the source of truth for enable and disable decisions
- request, integration-event, worker, polling, recovery, and scheduled execution paths enter through one canonical module-runtime gate rather than feature-local checks
- the gate remains responsible for both state confirmation and drain-participating work-lease acquisition
- route filters, dispatcher behaviors, handlers, outbox dispatch, and workers all stay inside the same canonical execution-control model

Regression expectations:

- architecture tests guard the canonical runtime entry points
- unit tests cover gate semantics
- integration tests cover persistence, activation, drain, and fail-closed behavior

### Integration delivery subsystem

Owner: baseline maintainers.

Canonical entry points:

- `AddPostgresIntegrationEventOutbox(...)`
- `IIntegrationEventOutboxPublisher`
- `IIntegrationEventOutboxDispatcher`
- `IntegrationEventOutboxHostedService`
- `IIntegrationEventInboxStore`

Invariants:

- module-owned store registration remains the only supported outbox composition path on the supported runtime
- delivery stays in-process through the shared integration-event dispatcher
- retry, leasing, and dead-letter policy remain centralized in the subsystem rather than feature-local workers
- inbox or dedupe persistence stays module-owned even while dispatch policy is shared

Regression expectations:

- architecture tests guard canonical registration and pump wiring
- integration tests cover outbox dispatch, inbox replay safety, retry, dead-letter, and bulkheads

### Scaffold and governance engine

Owner: baseline maintainers.

Canonical entry points:

- `scripts/New-Module.ps1`
- `templates/module/scaffold.contract.json`
- `scripts/Validate-Governance.ps1`
- `docs/RULE_TO_GATE_CATALOG.md`
- `docs/RULE_ENFORCEMENT_MAP.json`

Invariants:

- structural onboarding stays on one scaffolded path
- governance validation stays on one executable path
- scaffold outputs, governed docs, catalog, and workflow assets stay synchronized
- missing structural capability is fixed in the scaffold and governance engine, not by introducing a parallel hand-assembled path

Regression expectations:

- scaffold contract, governance-asset, and rule-catalog tests stay authoritative
- scaffold smoke and governance validation scripts remain part of the required validation wall

## Module Model

Each business module owns exactly five projects:

- .Api
- .Application
- .Domain
- .Infrastructure
- .PublicContracts

Every module has:

- one public `IApiModule` entry point in `.Api`
- one module descriptor containing key, display name, route prefix, schema name, module namespace, default-enabled flag, and can-be-disabled flag
- one dedicated PostgreSQL schema
- one DbContext in `.Infrastructure` when the module persists data
- one migrations history table inside that module schema
- one module namespace such as `identity` or `admin` for logging, telemetry, and configuration scoping
- one configuration section prefix under `Modules:{Module}`
- one frontend feature manifest entry when the module has UI surface

### Ownership By Project

`.Api` owns:

- transport request and response DTOs
- endpoint mapping
- auth requirements
- OpenAPI metadata
- Result-to-HTTP mapping

`.Application` owns:

- commands, queries, handlers, validators, authorization policies, and orchestration

`.Domain` owns:

- aggregates, entities, value objects, and invariants

`.Infrastructure` owns:

- DbContext
- repositories and persistence mappings
- migrations
- startup initializers
- outbox persistence and dispatch
- background workers
- external service adapters

`.PublicContracts` owns:

- versioned integration events
- public identifiers and public enums intentionally shared across modules
- stable cross-module read models and query contracts when another module must consume them

`.PublicContracts` does not own:

- browser transport DTOs
- endpoint-specific request shapes
- handler-specific commands or queries
- types that only the owning module needs internally

## Initial Modules

### 1. Platform

Platform is the operational control module and cannot be disabled.

Responsibilities:

- module catalog
- runtime module state transitions
- module bootstrap manifest for the frontend shell
- operational health summary
- audit event persistence and query surface
- operational safeguards around activation and administrative mutations

### 2. Identity

Identity is the production-grade authentication, account, and role-assignment module built on ASP.NET Core Identity, cookie authentication, and EF Core. It cannot be disabled.

Responsibilities:

- sign-in and sign-out
- current-user endpoint
- password change (self) and password reset (admin)
- account lockout and explicit admin unlock
- user administration and role assignment
- explicit operator session revocation via the ASP.NET Core Identity security stamp
- recent-auth step-up for sensitive mutations
- machine-principal authentication and machine-client lifecycle when machine access is enabled
- preferred time zone
- role seeding (`Admin`, `User`, `Machine`) during module initialization

### 3. Admin

Admin is the administrative feature module.

Responsibilities:

- user administration
- role assignment
- machine-client administration when enabled
- platform module control UI
- operational dashboards and system metadata

### 4. SampleFeature

SampleFeature is the scaffold-first EF reference module used to prove the add-module workflow.

It must be realistic enough to demonstrate:

- a real aggregate with EF Core persistence and migrations
- validation
- role-based authorization requirements
- public contracts
- an outbox event
- a frontend feature slice

### 5. Blog

Blog is the first production-grade EF-first teaching module.

It must demonstrate:

- a real aggregate with EF Core persistence and migrations
- taxonomy and editorial lifecycle workflows
- time-zone-aware scheduling and due-work processing
- module-owned outbox publication from an EF-backed module
- a matching public plus operator frontend slice

### 6. KnowledgeBase

KnowledgeBase is the more realistic end-user reference module.

It must demonstrate:

- a public read surface and operator management surface in one feature
- draft, publish, and archive lifecycle behavior
- generated browser contract usage
- a dedicated idempotent publish endpoint
- a versioned public integration event from `.PublicContracts`
- a module-owned outbox path that stays copyable for adopters

## ApiHost

ApiHost only:

- registers shared infrastructure
- wires authentication and authorization
- configures antiforgery, rate limiting, health, OpenAPI, and telemetry
- serves interactive API documentation through Scalar
- discovers modules and asks them to map endpoints
- hosts background services that are composition-only wrappers around module-owned workers
- initializes enabled modules and serves the frontend in production

ApiHost does not:

- contain business handlers
- contain persistence logic
- contain feature-specific endpoint behavior
- contain module-owned workers or startup routines

## Request And Execution Model

### Dispatcher Contracts

Build a dispatcher with these first-class contracts from day one:

- `ICommand`
- `ICommand<TResponse>`
- `IQuery<TResponse>`
- `ICommandHandler<TCommand>`
- `ICommandHandler<TCommand, TResponse>`
- `IQueryHandler<TQuery, TResponse>`
- `IIntegrationEvent`
- `IIntegrationEventHandler<TEvent>`
- `IRequestPipelineBehavior<TRequest, TResponse>`
- `IRequestValidator<TRequest>`
- `ITimeoutRequest`
- `IRequestExceptionHandler<TRequest, TResponse, TException>`
- `IRequestExceptionAction<TRequest, TException>`
- `IExceptionToErrorMapper`

### Required Request Behaviors

The default request pipeline order is explicit and stable:

1. Correlation and request-context enrichment
2. Cancellation short-circuit
3. Request timeout enforcement
4. Telemetry span start
5. Module-enabled enforcement
6. Authorization and actor checks
7. Idempotency lookup for opted-in commands
8. Validation
9. Transaction boundary for commands
10. Handler invocation
11. Integration-event capture and outbox persistence
12. Success telemetry completion
13. Exception translation and failure telemetry

Do not leave behavior order to container-registration accident.

### Idempotency

Idempotency is explicit infrastructure, not a controller-local cache.

Rules:

- only commands opt in to idempotency
- opted-in commands declare their idempotency policy through shared abstractions from day one
- persisted idempotency state includes at least `module_key`, `command_type`, `request_key`, caller identity, request hash, status, and stored outcome metadata
- the same key with a different effective payload fails deterministically as a conflict
- a completed command may return its stable prior outcome; an in-flight duplicate follows one documented policy such as wait, accepted, or conflict
- retention, expiration, and purge of idempotency records are explicit and tested

### Result, Exceptions, And HTTP Mapping

The runtime has one error vocabulary and two cooperating translation paths.

#### Mediator-owned path

- Commands and queries return `Result` or `Result<TResponse>`.
- Expected business failures return `Result` and do not throw.
- Unexpected exceptions are translated centrally by the dispatcher through `IExceptionToErrorMapper`.
- `OperationCanceledException` is rethrown.

#### Host-owned path

- A shared `IExceptionHandler` handles failures that occur before the dispatcher, after the dispatcher, or outside mediator execution.
- The host-level path covers malformed JSON, binding failures, antiforgery failures, middleware failures, rate-limit rejections, startup failures, and worker failures.

#### Shared writer

- `ResultHttpMapper` maps Result values at the API edge.
- `IExceptionHandler` uses the same shared ProblemDetails writer.
- The ProblemDetails writer defines the canonical payload shape and code vocabulary.

The host-level exception path never re-translates mediator-owned request failures. The dispatcher translates request exceptions; the host only handles failures that never became a Result.

### Required Exception Mapping Policy

The shared exception-to-error mapper supports at least:

- `DbUpdateConcurrencyException` -> `Conflict`
- PostgreSQL unique-constraint violations -> `Conflict`
- PostgreSQL foreign-key violations -> module-specific classification decided by explicit request policy, not ad hoc handler logic
- timeout and transient connectivity failures -> `ServiceUnavailable`
- authorization exceptions below the API edge -> `Forbidden` or `Unauthorized`
- unexpected exceptions -> internal `Failure`

Foreign-key violations are not classified ad hoc inside handlers. Each request type that can trigger one must declare the expected classification rule so the mapper stays deterministic across modules.

### Handler Authoring Rules

- Handlers return semantic `Result` values for expected failures.
- Handlers may catch a narrow exception only when they add business meaning that the shared mapper cannot know.
- Handlers do not catch `Exception` broadly.
- Handlers do not throw for expected validation, not-found, conflict, or authorization outcomes.
- Handlers do not log request start, success, or generic failure; the pipeline owns that.
- Handlers do not perform their own transaction orchestration.

## Event Model

Cross-module event communication uses integration events only. The baseline does not provide a shared framework contract for internal domain events. The supported default for intra-module reactions is explicit orchestration inside the owning module. If a module genuinely needs a local event mechanism, it stays module-owned and does not become a shared baseline abstraction or a cross-module side channel.

### Integration events

- Live in `.PublicContracts`
- Are the only event type another module may consume
- Are published after transaction commit via the owning module's outbox
- Are versioned and documented like any other public contract

Cross-module reactions flow through integration events only.

### Delivery semantics and replay safety

Cross-module delivery is at-least-once, not exactly-once.

Rules:

- every integration event carries a stable message id and occurrence timestamp
- consumers are replay-safe by design
- a consuming module uses inbox or dedupe persistence in its own Infrastructure whenever handler side effects are not naturally idempotent
- dedupe identity is at least `(message_id, consumer)`
- ordering guarantees are explicit and narrow; do not assume global ordering across modules
- dead-letter and replay procedures are documented for every consumer that touches external systems or non-commutative state

### Module state enforcement

- Module-owned commands and queries implement `IModuleScoped`.
- Module-owned integration-event handlers and module-owned workers also declare their owning module so runtime state flows through the shared execution gate outside HTTP.

## Cross-Module Interaction Model

Cross-module interaction is allowed, but it is not casual.

### Synchronous read path

Rules:

- synchronous cross-module calls are read-only
- a provider exposes only versioned PublicContracts query contracts or read models for synchronous reads
- a consumer calls the provider through an explicit query service or adapter, never through another module's DbContext, repository, or internal handler types
- every synchronous shared read declares freshness expectations, timeout budget, and caller fallback behavior
- request paths do not build deep cross-module fan-out trees; when repeated access is expected, prefer a local projection

### Process managers and long-running orchestration

Rules:

- workflows that cross modules, cross transactions, or require compensation use a persisted process manager
- process-manager state lives in the owning module schema (BP-012; enforced by `NoSharedRuntimeInboxOrCheckpointLiteralsRemainAnywhereInProductionSource` in `SharedAbstractionTests` and registered per module via `services.AddPostgresProcessManagerCheckpoints(moduleKey, schemaName)` — see `docs/adr/ADR-MODULE-OWNED-INBOX-AND-CHECKPOINTS.md`)
- process managers advance through explicit commands and integration events, not hidden side effects in endpoint code
- compensation rules, terminal failure rules, and operator-visible recovery steps are explicit and tested
- process managers emit audit events for material state transitions
- do not model cross-module business workflows as distributed transactions or ad hoc chains of unrelated event handlers

## Validation

Validation exists in three layers:

- transport validation at the API edge for malformed payloads and binding failures
- request validation in the dispatcher pipeline through `IRequestValidator<TRequest>`
- invariant enforcement in the domain model

Rules:

- validator support is mandatory in the dispatcher from day one
- a validator class is optional per request when no request-level rules exist
- validators are side-effect free
- validators may use light data access only when the rule genuinely belongs before handler execution
- validators do not replace domain invariants

## Data Architecture

### PostgreSQL

Use one PostgreSQL database with one schema per module.

Rules:

- each module owns only its own tables
- each module owns its own DbContext
- each module owns its own migrations
- schema isolation is enforced at the schema boundary; standard infrastructure table names may repeat across module schemas when each copy remains module-local
- mutable aggregates that support concurrent updates use optimistic concurrency tokens or equivalent write-version checks
- no cross-module foreign keys
- cross-module consistency uses orchestration or integration events, not distributed transactions
- cross-module reads use PublicContracts plus explicit query services or projections

### Migration Strategy

Each module owns its migrations in `.Infrastructure`.

Production strategy:

- a dedicated `DbMigrator` tool applies migrations before the app starts serving traffic
- the migrator acquires a PostgreSQL advisory lock so only one runner mutates schema at a time
- ApiHost verifies schema readiness at startup and fails fast if required migrations are missing

Development and test follow the same migrator-first posture or an explicit test harness that preserves the same readiness guarantees; runtime auto-apply is not part of the target architecture.

Rules:

- schema changes are rolling-deploy safe by default
- rename, drop, and new non-null requirements follow an expand-and-contract sequence
- application code is deployable against both the pre-change and post-expand schema during the compatibility window
- destructive cleanup runs only after old code paths and old clients are out of service

### Outbox

Every module that publishes integration events includes an outbox from day one.

Rules:

- module-owned outbox tables live in the owning module schema
- standard infrastructure table names such as `integration_outbox` may repeat across schemas; isolation still depends on schema ownership, not global table-name uniqueness
- only integration events enter the outbox
- dispatch uses row leasing or `FOR UPDATE SKIP LOCKED` semantics for multi-instance safety
- default batch size, retry policy, and dead-letter threshold are centralized and documented
- consumer handling is idempotent by contract

Recommended default envelope fields:

- id
- event_type
- event_version
- payload
- headers
- occurred_utc
- available_utc
- attempts
- leased_until_utc nullable
- processed_utc nullable
- last_error nullable

### Inbox and consumer state

Modules that consume integration events and need durable dedupe own an inbox in their own schema.

Rules:

- inbox records live in the owning module schema (BP-019/BP-020; enforced by `NoSharedRuntimeInboxOrCheckpointLiteralsRemainAnywhereInProductionSource` in `SharedAbstractionTests` and registered per module via `services.AddPostgresIntegrationEventInbox(moduleKey, schemaName)` — see `docs/adr/ADR-MODULE-OWNED-INBOX-AND-CHECKPOINTS.md`)
- inbox lifecycle is internal to the owning module
- a message is marked processed only after the handler's side effects commit
- replay, retry, and dead-letter administration remain module-owned

## Authentication And Authorization

### Authentication Model

Use ASP.NET Core Identity with cookie authentication. No JWT for browser sessions.

- secure HTTP-only auth cookie, `__Host-` prefixed, `SameSite=Lax`, `Secure=Always`, 8h sliding expiration
- no JWT bearer auth for the browser
- no browser token storage (no `localStorage` or `sessionStorage` tokens)
- ASP.NET Core Identity security stamp validation is enabled and drives session invalidation
- antiforgery is required for state-changing requests
- auth and antiforgery failures return normalized ProblemDetails responses
- same-origin browser access is the default and CORS stays disabled unless an ADR-backed allowlist explicitly names trusted origins; when enabled, origins, methods, headers, and exposed headers are all explicit wildcard-free allow-lists (see `docs/adr/ADR-CORS-CONFIGURATION.md`)
- shared Data Protection keys are persisted durably for multi-instance deployments
- seeded admin and seeded machine credentials are Development/Testing only with no override flag; any other environment fails fast at startup if seeded credentials are configured

### Machine-to-machine access

Machine access is optional, but it is governed from the start.

Rules:

- browser cookies are never reused as the machine-client model
- machine clients use a separate `Machine` auth scheme via `X-Machine-Key: {clientId}:{secret}`
- machine clients are persisted records with hashed secrets and full lifecycle (create, list, get, rotate, disable, reactivate, revoke)
- machine clients hold role assignments drawn from the same `IdentityRole` pool as users (typically `Machine`, optionally `Admin`)
- machine-consumable endpoints explicitly allowlist supported auth schemes and required role requirements
- machine authentication and privileged machine actions are audited and rate-limited

### Identity Authority Model

`Identity.Infrastructure` owns the ASP.NET Core Identity stores, user manager, role manager, and sign-in manager. The module boundary prevents other modules from reaching into those types.

Rules:

- ASP.NET Core Identity store entities stay in `Identity.Infrastructure` and never escape Infrastructure
- other modules consume Identity only through `Identity.PublicContracts` and the module's application-level contracts
- a field with business meaning has one authoritative owner
- framework-only auth fields such as password hash, security stamp, and access-failure counters remain infrastructure-owned
- business-facing fields such as display name, preferred time zone, and activation state are not duplicated across competing write models

### Authorization Model

Role-based authorization is the first-class primitive. The three built-in roles are:

- `Admin`
- `User`
- `Machine`

Rules:

- commands and queries declare role requirements via `RoleRequirement` entries in their `AuthorizationRequirements` collection
- the dispatcher authorization pipeline enforces role requirements centrally
- endpoint-level `[Authorize]` attributes supplement dispatcher-level checks at the API edge
- sensitive platform and admin mutations require explicit role enforcement plus recent-auth or equivalent step-up protection
- role assignment regression tests are required for privileged flows
- teams that need finer-grained authorization can add ASP.NET Core `IAuthorizationPolicy` policies or claim-based checks on top; the baseline does not ship with a custom permission catalog

### Session invalidation

Session invalidation uses ASP.NET Core Identity's standard security stamp.

Rules:

- role changes, password resets, and admin-triggered session revocations all bump the stamp via `UserManager.UpdateSecurityStampAsync`
- the cookie authentication handler validates the security stamp on every request and rejects stale cookies
- security stamp validation runs cross-instance through the shared durable Data Protection key store, so revocation takes effect on all nodes
- authorization fails closed when the current actor's security stamp cannot be verified
- disabled modules do not contribute active authorization to a live request; the shared module-execution gate short-circuits before role evaluation

## Runtime Module Activation

This is a first-class runtime capability, not a startup-only toggle.

### State Storage

State lives in PostgreSQL in a Platform-owned table such as `platform.module_state`.

Minimum columns:

- module_key
- desired_state
- runtime_state
- version
- transition_id
- updated_utc
- updated_by_user_id nullable
- last_error_code nullable
- last_error_detail nullable

`runtime_state` is one of:

- `Enabled`
- `Enabling`
- `Disabling`
- `Disabled`

### Safe-under-load semantics

Module transitions use optimistic concurrency plus a database lock so only one state transition per module runs at a time.

Disable flow:

1. Move the module to `Disabling`.
2. Reject new HTTP and dispatcher work immediately.
3. Stop module workers from dequeuing new work.
4. Allow in-flight work to finish within a bounded drain window.
5. Mark the module `Disabled`.

Enable flow:

1. Move the module to `Enabling`.
2. Run migrations and module initializers idempotently.
3. Start workers.
4. Mark the module `Enabled`.

If enable fails, the module returns to `Disabled`, the failure reason is recorded, and no partial live state remains.

### State propagation and drain coordination

Rules:

- all nodes observe module state through durable shared storage and read the current version directly at module-execution gate boundaries; optional push invalidation remains an optimization, not the required baseline
- startup does not activate module routes or workers until initial durable state load succeeds
- if a guard cannot confirm `Enabled` for the current version during `Disabling` or `Disabled`, it fails closed
- each node tracks per-module in-flight dispatcher requests and worker leases against the current `transition_id`
- stale acknowledgements or stale drain completions from an older transition are ignored
- a disable completes only when all live nodes acknowledge quiescence for the current transition or bounded recovery reclaims orphaned work

### Enforcement points

- route access checks happen per request
- dispatcher checks happen outside HTTP too
- module-owned event handlers, outbox dispatch, and workers respect module state through the shared execution gate
- repeated enable or disable requests are idempotent where safe
- disabled modules report `Disabled`, not `Unhealthy`
- `Enabling` and `Disabling` appear as degraded operational states

## Timezone And DST Strategy

Do not treat time as formatting only.

Use NodaTime throughout the backend.

Rules:

- store instants as `Instant` or UTC timestamp
- store business-local date concepts as `LocalDate`
- store business-local time concepts as `LocalTime`
- store timezone ids as IANA zone ids
- never use server local time for business decisions
- never use `DateTime.Now`

Identity stores each user's preferred IANA time zone.

### Required DST test ownership

- unit tests cover local-time calculation services and gap/ambiguity rules
- integration tests cover persistence, APIs, and scheduled behavior across DST boundaries
- shared time fixtures live in `BuildingBlocks.Testing`

Required regression cases:

- spring-forward gap handling
- fall-back ambiguous local times
- user preferred-timezone changes
- scheduled actions across DST boundaries

## Frontend Architecture

The frontend exists to present and orchestrate server capabilities, not to own domain rules.

### Structure

```text
web/src/
  app/
    store/
    router/
    providers/
    layout/
  features/
    auth/
    platform/
    admin/
    sampleFeature/
    knowledge-base/
  shared/
    api/
      generated/
      base/
    ui/
    lib/
    telemetry/
```

### Rules

- `app` is composition only
- features own their routes, RTK Query endpoint wrappers, UI state, and screens
- `shared` contains only truly reusable primitives
- components do not call `fetch` directly
- RTK Query is the only browser data-fetch mechanism
- generated API contracts are the source of truth for TypeScript request and response types
- no hand-maintained DTO mirrors of backend contracts
- browser route visibility uses public-route or role-based manifest requirements rather than permission-array gating
- feature routes are lazy-loaded by module manifest rather than bundled eagerly into the shell
- each feature route is wrapped in a feature-level error boundary so a crash in one module degrades only that module
- disabled modules do not eagerly preload or mount feature chunks
- browser real-time transport and cache invalidation go through one shared adapter, not feature-local websocket or SignalR clients
- business rules stay on the server unless they are purely presentational

### Contract sync

OpenAPI is the backend contract source for the browser.

Rules:

- backend endpoints declare explicit OpenAPI metadata
- CI generates the TypeScript client and fails if generated output is stale
- feature code wraps generated operations instead of retyping transport contracts by hand

### Browser telemetry

Browser telemetry is intentionally minimal.

Required scope:

- shell bootstrap failure before the app is usable
- route-level crash or React error-boundary failure
- chunk or asset load failure
- transport failure where no server response exists
- repeated session or antiforgery mismatch indicating client/server integration drift

Rules:

- one shared browser telemetry adapter owns emission
- feature code does not call a vendor SDK directly
- browser telemetry follows the same redaction policy as server logging
- telemetry failure degrades silently

### Real-time push

Real-time browser push is shared infrastructure, not a feature-local side channel.

Rules:

- one shared SignalR-based adapter owns connection lifecycle, reconnect policy, auth handoff, and telemetry
- modules publish typed notifications or cache-invalidation hints through shared abstractions instead of hub-local business logic
- browser push is read-oriented; state mutations still go through HTTP and the dispatcher
- auth, role, and module-state checks apply to subscriptions and message fan-out
- reconnect behavior, replay expectations, and fallback-to-refetch rules are explicit

## API Shape

Use minimal APIs only.

Rules:

- every endpoint goes through the dispatcher
- APIs use typed transport contracts in `.Api`
- API layer performs auth, binding, and response mapping only
- OpenAPI metadata is explicit for every endpoint
- endpoint filters and middleware use the shared ProblemDetails contract for non-success outcomes
- `.PublicContracts` never becomes a transport-DTO dumping ground

Include explicit metadata for:

- summaries
- descriptions
- success response types
- problem response types
- auth requirements
- rate-limit behavior where applicable

### HTTP versioning model

Rules:

- every externally consumed HTTP endpoint belongs to an explicit version set from its first release
- the default versioning shape is route-group versioning under `/api/v{major}` unless an ADR chooses a different external contract shape
- generated clients are emitted per supported HTTP version
- removals happen only after the declared deprecation and compatibility window expires

## Contract Lifecycle And Compatibility

Public contracts are products. They do not change casually.

### HTTP contracts

- additive changes are the default
- breaking changes require a new version or an explicit compatibility plan with dual support during the removal window
- deprecations record owner, reason, introduced date, target removal release, and replacement
- error codes and ProblemDetails shapes are treated as public contract
- every versioned request/response DTO under `*.Api.Contracts` (any type whose name ends with `V{n}`) carries `[ContractLifecycle]` declaring `IntroducedOn` and `Status` (`Active` or `Superseded`)
- a superseded HTTP contract must declare `SupersededBy` (the replacement type) and `RetireOn` (the hard removal deadline); `RetireOn` must fall after the successor's `IntroducedOn` and within `ContractLifecyclePolicy.MaxCompatibilityWindowDays` (365 days), and past-due lifecycles fail the architecture gate
- at most one `Active` version may exist per HTTP contract base name; older versions are marked `Superseded` so the removal window has an explicit owner and expiry

### Integration contracts

- integration events are versioned from the first release
- additive payload evolution stays within the current version
- semantic or field-removal breaking changes ship as a new event version
- when consumers need time to migrate, publishers dual-publish old and new versions during the compatibility window
- dual-publish during a compatibility window is the default live-delivery strategy; generic implicit runtime upcasting is not the primary compatibility mechanism
- when long-retained replay or dead-letter tooling must rehydrate historical payloads beyond the dual-publish window, explicit tested upcasters live at the replay boundary, not inside domain handlers
- every concrete `IIntegrationEvent` under `*.PublicContracts.Events` carries `[ContractLifecycle]` declaring `IntroducedOn` and `Status` (`Active` or `Superseded`)
- a superseded version must declare `SupersededBy` (the replacement type) and `RetireOn` (the hard removal deadline)
- `RetireOn` must fall after the successor's `IntroducedOn` and within `ContractLifecyclePolicy.MaxCompatibilityWindowDays` (365 days) — past-due lifecycles fail the architecture gate and force removal of the obsolete type and its handlers
- at most one `Active` version may exist per event base name; older versions must be marked `Superseded` so the dual-publish window has an explicit owner and expiry

### Module-to-module contract tests

- providers publish contract fixtures or snapshots for shared query contracts and integration events
- consumers run compatibility tests against provider fixtures in CI
- breaking changes are blocked unless a new version or compatibility window is present

## Configuration Management

Configuration is part of the architecture, not an implementation afterthought.

Rules:

- each module owns a namespaced configuration section such as `Modules:{Module}`
- configuration binds to typed options in ApiHost or Infrastructure, not raw `IConfiguration` use in Application or Domain
- required settings validate at startup and fail fast when invalid
- only explicitly declared operational settings may reload at runtime
- runtime configuration changes that affect behavior or security are audited
- secrets come from environment or secret stores and are never committed to the repo

## Operational Resilience And Bulkheading

One module may fail or saturate without becoming a whole-app denial of service.

Rules:

- synchronous cross-module reads have explicit timeout, cancellation, and fallback budgets
- workers, outbox dispatchers, inbox processors, and process managers have per-module concurrency limits or queue isolation
- external service adapters declare timeout, retry, and circuit-break policy explicitly
- one module's backlog or failing dependency must degrade that module before it starves unrelated module paths
- resilience settings are visible, testable, and owned by the module that depends on them

## Observability And Audit

### Logging

Every important runtime transition logs structured data.

Minimum common fields:

- correlation_id
- request_id
- trace_id
- module_key
- request_type
- route
- user_id when known
- outcome
- error_code when relevant

### Audit events

Audit events are distinct from ordinary logs.

Architecture:

- `BuildingBlocks.Application` exposes a shared `IAuditEventWriter`
- `Platform.Infrastructure` persists canonical audit events in `platform.audit_event`
- modules emit audit events through the shared abstraction, not by calling logging sinks directly
- audit records are append-only and ordinary business behavior does not update or delete them

Minimum audit fields:

- actor
- action
- target_type
- target_id
- timestamp
- outcome
- correlation context
- module_key

Required audit events include:

- login and logout
- lockout and unlock
- password reset request and completion
- user activation and deactivation
- role assignment and removal
- role assignment change
- module enable and disable attempts
- module enable and disable outcomes, including rollback
- critical administrative configuration changes

### Sensitive-data rules

Never log:

- passwords or reset secrets
- auth cookies
- antiforgery tokens
- raw authorization headers
- raw session identifiers
- connection strings or secret values
- full request bodies by default

## Background Processing

Background jobs are module-owned.

Rules:

- workers live in module Infrastructure
- workers check module state before dequeue and before execution
- worker loops have explicit exception boundaries
- retryable and terminal failures are distinguished explicitly
- outbox workers use the shared exception-classification policy
- poison-message or terminal-failure behavior is defined up front
- process managers checkpoint durable progress before acknowledging external work when replay matters

## Testing Strategy

TDD is mandatory.

The baseline ships with:

- unit tests for domain logic, validation, role-based authorization, and timezone rules
- architecture tests for structural rules and shared-layer allowlists
- integration tests for auth, module activation, ProblemDetails normalization, outbox flow, inbox/idempotency, health, audit, security-stamp-based session invalidation, process-manager recovery, and DST-sensitive behavior
- frontend unit tests and E2E tests for shell, auth, admin, module-aware code splitting, shared real-time behavior, and normalized failure behavior
- contract tests for OpenAPI generation, HTTP versioning, shared query-contract compatibility, integration event compatibility, and TypeScript client freshness
- migration safety tests for rolling deployments and destructive-change sequencing
- multi-instance activation tests for state propagation, bounded drain, and fail-closed behavior on uncertainty
- resilience tests for synchronous-read timeout policy and module-level bulkheads
- scaffold smoke tests that prove a new module can be generated in the governed shape without manual structural edits

## Implementation Order

1. Create repo skeleton, package/version management, editorconfig, CI, ADR folder, rule-to-gate catalog, waiver model, and scaffold smoke harness.
2. Build shared BuildingBlocks abstractions, dispatcher, Result model, ProblemDetails writer, idempotency primitives, process-manager abstractions, audit abstraction, real-time abstractions, and architecture-test harness.
3. Build `DbMigrator`, PostgreSQL schema conventions, outbox, inbox/dedupe, rolling-deploy helpers, and runtime module-state infrastructure with locking, propagation, and drain semantics.
4. Build Platform module with module catalog, bootstrap manifest, audit store, module-state control plane, and runtime activation APIs.
5. Build Identity module with ASP.NET Core Identity, cookie auth, role seeding, optional machine-principal auth, antiforgery, current-user flow, and a production-grade administrator-account provisioning model.
6. Build Admin module with role-based user administration, optional machine-client administration, runtime module-management UI, and operational controls.
7. Build frontend shell, generated contract pipeline, RTK Query base layer, shared real-time adapter, auth hydration, module-aware code splitting, and role-aware routing.
8. Add SampleFeature to prove the deterministic scaffold and governed add-module workflow, then add Blog as the first bounded EF-first teaching slice, then add KnowledgeBase to prove the same architecture in a more realistic public-facing module with idempotent publishing and a public integration event.
9. Add telemetry, DST regression fixtures, resilience and bulkhead tests, and remaining operational hardening.

## Definition Of Done For The Baseline

The baseline is not done until a new module can be added without changing the underlying architecture.

That means the baseline already contains:

- the governed shared-layer model
- the module template shape
- the runtime activation state machine
- the migration strategy
- the cookie-auth and antiforgery model
- the role-based authorization model
- the time-handling model
- the generated contract flow for the frontend
- the HTTP and integration contract lifecycle policy
- the synchronous cross-module read model
- the process-manager model for long-running workflows
- the dispatcher pipeline
- the durable idempotency model
- the inbox and replay-safety model for at-least-once consumers
- the security-stamp-based session invalidation model
- the configuration-management model
- the shared real-time push model
- the module-aware frontend loading model
- the rolling-deploy-safe migration rules
- the inter-module bulkheading rules
- the audit architecture
- the module-to-module contract test model
- the rule-to-gate traceability catalog
- the deterministic module scaffold
- the architecture, contract, and CI gates

If the second real module still forces a structural retrofit in any of those areas, the baseline was not finished.
