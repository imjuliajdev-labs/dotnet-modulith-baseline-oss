# Add A Module

This baseline assumes every module is added with the same structure, the same ownership rules, and the same gates.

If adding a module requires changing the architecture first, the baseline is incomplete.

## Decide These Inputs Before Scaffolding

For every new module, decide up front:

- module key
- module owner
- display name
- route prefix
- PostgreSQL schema name
- whether the module is core or optional
- default enabled state
- module namespace — the route prefix and metadata key family for the module, e.g. `"platform"`, `"blog"`; it is module metadata, not an authorization concept
- whether the module publishes integration events
- whether the module consumes integration events
- whether the module exposes synchronous read contracts to other modules
- whether the module needs a long-running process manager
- whether the module has opted-in idempotent commands
- whether the module has background workers
- whether the module introduces externally consumed HTTP contracts
- whether the module exposes machine-consumable endpoints
- whether the module emits browser real-time notifications
- which existing modules, if any, this module is expected to reference through `*.PublicContracts`, expressed as `crossModulePublicContractDependencies`
- module configuration section name and any typed operator-managed setting definitions, including their names, display labels, primitive types, default values, and whether each definition is reload-safe
- whether the module exposes operator-managed runtime settings beyond startup-only configuration
- which frontend RTK Query tag names, expressed as PascalCase helper names, should be invalidated when operator-managed settings changes fan out into browser-visible reads
- whether the module has sensitive mutations that require recent-auth or equivalent step-up protection
- whether the module has frontend surface

Do not scaffold the module until these decisions are explicit.

If `crossModulePublicContractDependencies` is non-empty, run `pwsh scripts/Report-ModuleDependencies.ps1` before scaffolding so the current BP-033 headroom is visible before the new reference is introduced. The scaffold surfaces the new module's projected headroom, but it does not replace the repo-wide report.

## Scaffolding Contract

Add a module only through the repo's supported scaffold command or template. Manual structural assembly is not the supported path.

The scaffold and governance engine are a baseline-owned platform subsystem. If the supported path is missing a required structural capability, extend `scripts/New-Module.ps1`, `templates/module/*`, or the governance checks instead of introducing a second module-onboarding path.

The scaffold must generate:

- the five backend projects
- the module descriptor and public `IApiModule` entry point
- module descriptor metadata that declares whether the module has frontend surface
- typed configuration shells for `.Api` and `.Infrastructure`
- typed operator-managed setting-definition metadata when the module spec declares runtime settings
- a compile-ready EF Core persistence shell in `.Infrastructure`, including persistence defaults, `DbContext`, a baseline entity-configuration example, provider wiring, a design-time factory, and a migration runner anchor
- a compile-ready infrastructure configuration extension that binds the module section through typed options and validates defaults on startup
- an opt-in runtime settings query/update shell that emits audit events when the module declares operator-managed settings
- compile-ready unit, integration, and architecture tests for descriptor, bootstrap manifest, and runtime shape coverage
- an architecture guardrail that anchors typed configuration binding and startup validation for the generated module
- guardrail-compliant anti-placeholder capability coverage anchors for any selected event-publication, event-consumption, or process-manager capability
- shared query-contract or integration-contract compatibility tests when the module exposes them
- process-manager, idempotency, worker, machine-endpoint, or real-time shells when those capabilities were selected as module inputs
- a module-owned recent-auth policy shell when the module declares sensitive browser mutations that require step-up
- a frontend feature manifest and lazy feature component shell when the module has UI surface
- a frontend operational-settings panel shell, feature-owned RTK Query settings tag-helper file, RTK Query settings wrapper, a dependent-read projection shell, frontend unit-test anchors, and a minimal Playwright anchor when the module has UI surface and declares operator-managed settings
- scaffolded frontend anchors that already execute in the supported temporary-workspace frontend validator when the module has UI surface and operator-managed settings
- solution membership updates when the scaffold runs against a repo root that contains the solution file
- waiver placeholders only when explicitly requested

The scaffold output must be deterministic and rerunnable. If the scaffold cannot create the module without hand-editing the base architecture, fix the baseline first.

Scaffolded advanced-capability tests are baseline anchors, not final acceptance coverage. They must already satisfy the anti-placeholder architecture guardrails on generation, and then they are expected to be expanded into module-specific behavioral coverage before merge.

When the scaffold runs against the repository root, the structural integration path is automatic:

- the new backend projects are added to the solution
- `ApiHost` and the integration host discover the new `.Api` assembly through project references and `AddApiModulesFromAssemblyReferences(...)`
- frontend feature discovery and reconciliation flow through the module descriptor metadata plus feature manifest files under `web/src/features/**/index.ts`

Feature behavior, endpoints, persistence, and business rules still arrive later as module implementation work. Structural onboarding does not.

## Required Projects

Create one folder under `src/Modules/{Module}` with exactly these projects:

- `{Module}.Api`
- `{Module}.Application`
- `{Module}.Domain`
- `{Module}.Infrastructure`
- `{Module}.PublicContracts`

## What Belongs Where

`{Module}.Api` owns:

- transport DTOs
- endpoint mapping
- auth declarations
- OpenAPI metadata
- Result-to-HTTP mapping

`{Module}.Application` owns:

- commands
- queries
- handlers
- validators
- authorization rules
- orchestration

`{Module}.Domain` owns:

- aggregates and entities
- value objects
- invariants

`{Module}.Infrastructure` owns:

- DbContext and persistence mappings
- repositories and adapters
- migrations
- outbox persistence and dispatch
- startup initializers
- background workers

`{Module}.PublicContracts` owns only:

- versioned integration events
- stable public identifiers and enums
- public read models or query contracts intentionally shared across modules

`{Module}.PublicContracts` never owns:

- API request DTOs
- API response DTOs used only by browser clients
- handler-internal commands or queries
- types that no other module consumes

## Required Runtime Shape

- Add one public `IApiModule` implementation in `{Module}.Api`.
- Register dispatcher handlers and validators from `{Module}.Application` in `AddServices`.
- Declare the module namespace through the module descriptor.
- Make module-owned commands and queries implement `IModuleScoped`.
- Ensure module-owned workers and integration-event handlers declare the owning module so runtime state applies outside HTTP.
- Map endpoints only from `{Module}.Api`.
- Put persistence only in `{Module}.Infrastructure`.
- Register module startup tasks from `{Module}.Infrastructure` via `IModuleInitializer` when initialization is required.
- Make initializers idempotent so enable/disable cycles are safe.
- Declare idempotency policy for opted-in commands through the shared abstractions.
- Declare module-owned recent-auth defaults in Application and reuse them from sensitive request contracts when step-up is required.
- Expose synchronous shared reads only through `{Module}.PublicContracts` and module-owned query services.
- Keep process managers and their state in the owning module when a workflow spans modules or time.
- Bind and validate module configuration through typed options in `{Module}.Api` or `{Module}.Infrastructure` only. The scaffold now emits the default Infrastructure binding anchor; extend it rather than bypassing it.
- Declare any planned cross-module `*.PublicContracts` dependencies in `crossModulePublicContractDependencies` before scaffolding so BP-033 headroom is explicit up front instead of being discovered only after references are added.
- When the module exposes operator-managed settings, declare them in the module spec as typed definitions instead of a bare list of setting names so the scaffold can emit richer configuration and UI metadata.
- When the module needs relational persistence, extend the scaffolded EF Core persistence shell in `{Module}.Infrastructure/Persistence`, replace the baseline entity-configuration example with the first real mapping, and avoid copying another module's store implementation as a starting point.
- Declare frontend surface on the module descriptor so feature manifests can be reconciled against the runtime module set.
- Use the shared browser real-time adapter when the module emits push notifications.
- Expose cross-module types only from `{Module}.PublicContracts`.
- Use `CursorPagedResult<T>` and `CursorEncoding` from `BuildingBlocks.Application` for public-facing list endpoints. Do not use offset pagination.
- Apply rate limiting policies to security-sensitive endpoints (authentication, password mutations) using the existing `IdentityEndpointPolicies` as a reference. Each module owns the registration of its own rate-limiter policies by implementing `IModuleRateLimiterContributor` from `BuildingBlocks.Infrastructure.Modules`; `ApiHost` never names a module. `Identity` and `Platform` are the reference implementations.
- Register `IDbContextRollbackRegistration` for the module's `DbContext` if the module uses EF Core persistence.

## Event Rules

- If another module needs to react, publish a versioned integration event from `{Module}.PublicContracts`.
- Cross-module consumers subscribe only to versioned integration events; they never depend on another module's internal state-change notifications.
- Outbox messages are created only for integration events or external-delivery contracts.
- Cross-module consumers assume at-least-once delivery and are replay-safe.
- A consuming module uses inbox or dedupe persistence in its own Infrastructure when side effects are not naturally idempotent.
- Breaking event payload changes ship as a new event version, not as an in-place mutation.
- If historical replay across versions must outlive a dual-publish window, provide explicit tested upcasters at the replay boundary.

## Contract Lifecycle Annotation Rules

Every versioned public contract carries a `[ContractLifecycle]` attribute from `BuildingBlocks.Domain.Contracts`. This applies to two discovery surfaces:

- concrete integration events under `{Module}.PublicContracts.Events` that implement `IIntegrationEvent`
- versioned HTTP request/response DTOs under `{Module}.Api.Contracts` whose type name ends with `V{n}`

When introducing a new version:

1. Annotate the new type as `[ContractLifecycle("YYYY-MM-DD", ContractLifecycleStatus.Active)]` with today's UTC date as `IntroducedOn`.
2. Flip the previous version to `ContractLifecycleStatus.Superseded`, add `SupersededBy = typeof(NewType)`, and set `RetireOn` to the hard removal deadline. `RetireOn` must fall after the successor's `IntroducedOn` and within 365 days (`ContractLifecyclePolicy.MaxCompatibilityWindowDays`).
3. At most one `Active` version may exist per contract base name at any time. The architecture gate fails the build if a second one is introduced without superseding the older.
4. On the day after `RetireOn`, the gate fails the build. The expected fix is to delete the obsolete type and its handlers or endpoints, not to extend the deadline. Extending a deadline requires a dated waiver entry per the governance rules.

The scaffold generates new events and the `{Module}ContractV1` baseline DTO with the attribute already applied, so freshly scaffolded modules are compliant on day one.

## Cross-Module Interaction Rules

- Synchronous shared reads are read-only, explicit, versioned, and bounded by timeout and fallback policy.
- No synchronous cross-module write path is introduced.
- Repeated or latency-sensitive cross-module reads prefer projections over deep call chains.
- Long-running cross-module workflows use a persisted process manager with explicit compensation and recovery rules.
- Shared query contracts and integration contracts get consumer/provider compatibility coverage before merge.
- When another module actually consumes a shared query contract or integration event, that consumer carries a compatibility test that references the provider's public contract namespace.

## Required References

- `ApiHost` references only shared BuildingBlocks projects allowed for the host and module `.Api` projects.
- `{Module}.Api` may reference its own `.Application`, `.Infrastructure`, `.PublicContracts`, and allowed shared BuildingBlocks projects.
- `{Module}.Application` may reference its own `.Domain`, `.PublicContracts`, and allowed shared BuildingBlocks projects.
- `{Module}.Infrastructure` may reference its own `.Application`, `.Domain`, `.PublicContracts`, and allowed shared BuildingBlocks projects.
- `{Module}.PublicContracts` references only shared BuildingBlocks abstractions needed for public contracts.
- No module may reference another module except through `.PublicContracts`.

## Required Data And Operations Shape

When the module persists data, it must define:

- one DbContext in `.Infrastructure`
- one PostgreSQL schema
- one migrations history table in that schema
- module-local infrastructure tables may reuse standard names such as `integration_outbox` and `__EFMigrationsHistory`; isolation is enforced by schema boundary, not global table-name uniqueness
- optimistic concurrency tokens or equivalent write-version checks on mutable aggregates where concurrent writes matter
- zero cross-module foreign keys
- rolling-deploy-safe migrations that follow an expand-and-contract sequence
- no cross-module transaction coordination through shared DbContexts or distributed transactions

The scaffold emits an EF Core persistence shell plus a baseline entity-configuration example by default so new modules start from the governed shape even before real aggregates, mappings, and migrations are added.

When the module is optional, it must also support:

- route guarding per request with canonical `503` ProblemDetails responses for disabled or disabling HTTP requests
- dispatcher enforcement outside HTTP
- worker shutdown before new work is accepted during disable
- idempotent enable initialization

## Required Frontend Shape

If the module has UI surface:

- add one feature folder under `web/src/features/{module}`
- wrap generated API contracts with RTK Query endpoints owned by that feature
- declare any settings fanout tags in the module spec so scaffolded settings updates can invalidate dependent feature reads through a generated feature-owned tag-helper file without editing the shared base API
- rerun `scripts/Generate-Contracts.ps1` after adding or changing module HTTP endpoints so feature-owned wrappers bind to the refreshed generated contract types
- use `scripts/Test-ModuleScaffoldFrontend.ps1` to lint, typecheck, run the generated frontend unit-test anchors, and execute the generated Playwright anchor in a temporary scaffold workspace
- keep feature routes, screens, and UI state inside that feature
- lazy-load the feature from the module manifest instead of bundling it eagerly into the shell
- use the shared real-time adapter for push invalidation or notifications instead of feature-local websocket clients
- use shared auth, ProblemDetails, and telemetry adapters instead of feature-local equivalents
- do not duplicate backend transport contracts by hand in TypeScript
- use React Hook Form + Zod for form validation with `aria-invalid` and `aria-describedby` on all validated inputs; do not use inline submit-button-disable patterns

## Required Tests Before Merge

- `pwsh scripts/Invoke-LocalGates.ps1` is the canonical local pre-merge wall; use it first unless you are deliberately re-running a narrower subset after a fix
- targeted reruns go through the canonical gate dispatcher, for example:
  - `pwsh scripts/Invoke-CiGate.ps1 -Id architecture-tests`
  - `pwsh scripts/Invoke-CiGate.ps1 -Id integration-tests`
  - `pwsh scripts/Invoke-CiGate.ps1 -Id contract-generation-and-compatibility`
- when the module change needs a gate that defaults to `skipLocally` (for example Playwright E2E), opt into it explicitly with `pwsh scripts/Invoke-LocalGates.ps1 -IncludeSkipped` or re-run the exact gate id once the local prerequisites exist
- module unit tests for domain rules and handler-centric logic where direct tests are the clearest fit, with every handler also covered by either a corresponding test class or a governed handler-coverage inventory entry before merge
- request validation, authorization, recent-auth, and ProblemDetails behavior are covered for module endpoints that expose sensitive browser mutations
- idempotency coverage exists for any opted-in command
- contract generation succeeds and generated client output is current
- shared query-contract compatibility coverage exists when the module exposes synchronous reads to other modules
- no pending EF model changes for the module
- optimistic concurrency and migration-safety coverage exists when the module persists mutable state
- duplicate-delivery and replay-safety coverage exists when the module consumes integration events
- process-manager and compensation coverage exists when the module coordinates long-running workflows
- optional-module enable and disable coverage exists when the module can be disabled
- machine-auth coverage exists when the module exposes machine-consumable endpoints
- real-time subscription and authorization coverage exists when the module emits browser push
- typed configuration validation coverage exists for required module settings
- runtime settings shell coverage exists when the module declares operator-managed settings
- frontend settings-flow unit-test coverage exists when the module declares operator-managed settings and has UI surface
- frontend managed-settings Playwright anchor coverage exists when the module declares operator-managed settings and has UI surface
- frontend unit and E2E coverage when the module has UI surface

If any required gate fails, the module is not ready.

## Review Checklist

Before merging a new module, confirm:

- PublicContracts contains only real public contracts; every query namespace includes a governing service interface and every non-interface type is reachable from it
- no browser DTO leaked into PublicContracts
- no orphaned read models or response records that are not part of a query service interface surface
- no cross-module write path was introduced
- any module-specific commands and queries declare the correct `RoleRequirement` entries rather than relying on endpoint-only checks
- any integration event is versioned and documented
- every new public HTTP or event contract has a versioning and deprecation story
- any synchronous shared read has declared freshness, timeout, and fallback behavior
- any planned cross-module `*.PublicContracts` dependency was declared in `crossModulePublicContractDependencies`, checked with `pwsh scripts/Report-ModuleDependencies.ps1`, and kept within BP-033 headroom before implementation started
- any consumer of integration events is replay-safe and uses inbox or dedupe where required
- any long-running cross-module workflow uses a process manager rather than hidden handler chaining
- scaffolded advanced-capability test anchors have been replaced or expanded with module-specific behavioral assertions before the module ships
- module runtime state works correctly if the module is optional
- migrations, outbox, and workers all live in Infrastructure
- persistence changes are safe for rolling deployment
- module configuration is namespaced, typed, validated, and audited when runtime changes matter
- machine-consumable endpoints use the separate machine auth path when present
- frontend surface is lazy-loaded by module and uses shared real-time infrastructure when push exists
- sensitive browser mutations use explicit role requirements and recent-auth or equivalent step-up protection when applicable
- the supported scaffold produced the module shape without ad hoc structural edits
- the module can be added without changing the base architecture

See the blueprint and quality gates documents for the governing architecture and enforcement rules this workflow relies on.
