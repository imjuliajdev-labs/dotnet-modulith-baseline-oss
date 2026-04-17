# Quality Gates

A change is incomplete unless all required gates pass or an approved waiver exists.

## Governance Rule

Every non-negotiable in the blueprint must map to one of these enforcement paths:

- executable architecture test
- executable integration or E2E test
- analyzer or linter rule
- generated-artifact freshness check
- dated waiver entry with owner, reason, linked follow-up, and expiry

If a rule has no enforcement path, it is not finished enough to be called a non-negotiable.
The repo keeps a versioned rule-to-gate catalog, and any non-negotiable change updates that mapping in the same review.

## Waiver Policy

Temporary exceptions are explicit and ratcheted.

Rules:

- waivers live in versioned allowlist files, not in prose
- each waiver includes owner, creation date, expiry date, and removal condition
- a waiver may suppress only a named rule, not a whole category of failures
- expired waivers fail the build
- no PR merges on a yellow or partially skipped structural gate

Canonical waiver fields:

- `waiver_id`
- `rule_id`
- `scope`
- `owner`
- `created_utc`
- `expires_utc`
- `reason`
- `linked_follow_up`
- `removal_condition`

Enforcement:

- `WaiverValidationTests` deserializes `waivers/active.waivers.json`, asserts every entry references a `rule_id` that exists in the rule-to-gate catalog, asserts every `expires_utc` is strictly in the future, validates all required fields are non-empty, and rejects duplicate `waiver_id` values
- the test short-circuits to pass when the waivers array is empty, so an empty baseline is legal

Operational note:

- the baseline begins with validated waiver files and expiry enforcement, not speculative rule-skip plumbing
- rule-specific suppression or conditional skip behavior is added only when an approved waiver actually needs it

## Spec And Scaffold Gate

The docs and the scaffold are executable governance assets.

Required checks:

- `BLUEPRINT.md`, `QUALITY_GATES.md`, `ADD_MODULE.md`, `RULE_TO_GATE_CATALOG.md`, `RULE_ENFORCEMENT_MAP.json`, `README.md`, `AGENTS.md`, and affected ADRs remain internally consistent
- maintained narrative docs declared in `docs/README.md` exist, carry the maintained-doc tier marker, and pass lightweight freshness checks for known status claims and reference links
- durable markdown docs under `docs/**` plus `README.md` and `AGENTS.md` pass relative-link integrity checks and reference only known `BP-###` rule ids
- every non-negotiable is listed in the versioned rule-to-gate catalog with an enforcing artifact or waiver id
- rule-enforcement map artifacts stay inside governed executable, script, or lint families
- foundational architecture changes include a matching ADR update under `docs/adr`
- waiver files validate against the canonical schema and reference known `rule_id` values
- host and migrator module discovery stay anchored to declared `.Api` project references; loose output-directory assembly scanning is not a supported composition path
- the supported module scaffold command can generate a compile-ready module shell in the governed shape, including recent-auth policy shells for step-up modules, guardrail-compliant anti-placeholder capability coverage anchors for selected advanced capabilities, and, when a solution file is present, update solution membership without hand-editing composition files
- the supported module scaffold command emits a compile-ready EF Core persistence shell for module Infrastructure, including persistence defaults, `DbContext`, a baseline entity-configuration example, provider wiring, design-time factory, and migration runner anchors
- the supported module scaffold command emits typed configuration binding and startup-validation anchors so new modules do not start from raw configuration access
- when a module spec opts into operator-managed settings, the supported scaffold command consumes typed operator-managed setting definitions and emits the matching audited query/update shell plus richer configuration and frontend settings metadata where applicable
- when a module spec supplies settings fanout tags, the supported scaffold command emits a feature-owned RTK Query tag-helper file plus enhancement and invalidation wiring without hand-editing the shared base API
- when a module spec opts into operator-managed settings and UI surface, the supported scaffold command emits frontend unit-test anchors for the scaffolded settings flow, settings-tag helpers, and RTK Query invalidation behavior
- module-add workflow guidance, the shared add-module prompt, the module spec example, and the scaffold command make planned cross-module `*.PublicContracts` dependencies explicit through `crossModulePublicContractDependencies` and surface BP-033 headroom through `scripts/Report-ModuleDependencies.ps1` before new references are introduced
- module-add workflow guidance requires contract refresh after module HTTP surface changes so feature-owned wrappers converge on generated transport types instead of scaffold fallbacks
- the supported config-first adoption workflow dispatches validation profiles through the canonical local gate runner and gate manifest instead of maintaining inline raw command lists that can drift from CI
- the supported guided adoption wrapper (`scripts/Start-Adoption.ps1`) collects unresolved first-time inputs, explains the optional teaching modules, points adopters to the detailed docs before they choose, writes a versioned adoption spec into the target repo, runs the same governed dry run before mutation, preserves cloned git history while aligning the target with the current working tree snapshot, and delegates all rename and module-removal work to `scripts/Adopt-Baseline.ps1` instead of re-implementing it
- the supported config-first adoption workflow keeps `scratch.root` inside the declared repo-local or `os-temp` base and exports the resolved path to downstream governance scripts through `DOTNET_MODULITH_SCRATCH_ROOT` during validation so scratch output stays co-located
- the supported frontend scaffold validator lints, typechecks, executes generated frontend unit-test anchors, and executes generated Playwright anchors inside a temporary scaffold workspace
- scaffold output is regression-checked so structural drift shows up in CI
- the supported config-first adoption workflow ships with a versioned example spec and schema, and the supported adoption script must execute a dry-run preflight successfully against that example spec
- the supported guided adoption wrapper must execute successfully in a governed non-interactive dry-run smoke against a temporary target path while validating the current working tree snapshot so the first-time human path stays live
- user-facing governance scripts (`Generate-Contracts.ps1`, `Test-ModuleScaffold.ps1`, `Test-ModuleScaffoldFrontend.ps1`, `Test-ApiHostBootstrap.ps1`, `Test-ApiHostMissingMigrations.ps1`) route scratch output through the shared `New-GovernanceScratchDirectory` / `New-GovernanceScratchFilePath` helpers so every run lands under a single repo-relative scratch root (`.tmp/` by default, or the path in `DOTNET_MODULITH_SCRATCH_ROOT`); raw `GetTempPath()` calls are not a supported pattern in these scripts
- the scaffold and governance engine remain the one supported structural onboarding and governance-validation path; alternate structural or governance paths are not a supported extension model

## Build Gate

- `dotnet restore`
- `dotnet build` with warnings treated as errors
- analyzers enabled for solution projects
- `dotnet format whitespace --verify-no-changes` is clean across the solution (line-ending and BOM normalization is enforced; no in-PR drift between Windows and Linux contributors)
- `pnpm install --frozen-lockfile`
- frontend build succeeds in CI mode

## Architecture Gate

The architecture gate verifies, through architecture tests, analyzers, or source inspections as mapped in the rule-to-gate catalog:

- required module project layout
- exactly one public `IApiModule` entry point per `.Api` assembly
- ApiHost dependency boundaries and composition-only policy
- cross-module references target only `.PublicContracts`
- module service-registration entry points do not reference other module namespaces outside `.PublicContracts`
- shared BuildingBlocks projects obey their allowlisted dependency and content boundaries, and their approved shared public surface changes only through explicit admission updates
- the dispatcher subsystem keeps one canonical registration and compiled-invoker execution path
- the module runtime subsystem uses one canonical execution gate across dispatcher, integration-event, outbox-dispatch, and worker entry points instead of hidden parallel composition paths
- the integration delivery subsystem keeps one canonical outbox or inbox composition path and dispatch pump on the supported runtime path
- `.PublicContracts` do not contain browser transport DTOs or handler-internal request contracts
- `.PublicContracts` query namespaces contain at least one governing service interface, and every non-interface type is reachable from a service interface or composes reachable types
- dispatcher marker usage on commands and queries
- module API endpoints route use cases through the dispatcher except explicit infrastructure endpoints such as antiforgery token issuance
- module-owned commands and queries declare their owning module via `IModuleScoped`
- handler registration completeness after module registration
- DbContexts and migrations exist only in module Infrastructure
- no controllers
- module APIs do not access EF Core or persistence types directly
- ApiHost composes the central browser-mutation antiforgery path before authorization
- platform operational mutation endpoints require explicit admin protection
- platform operational mutation endpoints require explicit rate limiting
- dispatcher pipeline registration contains the required behaviors in the required order
- opted-in commands declare idempotency policy through shared abstractions, not endpoint-local caches
- ApiHost registers the shared host-level exception path and shared ProblemDetails writer
- handlers do not catch broad `Exception`
- expected ProblemDetails responses do not bypass the shared mapper and writer
- shared logging abstractions do not expose banned sensitive-field logging APIs where enforceable
- security-sensitive operations publish audit events through the shared audit abstraction where enforceable
- business code uses shared time abstractions and NodaTime types instead of server-local time APIs where enforceable
- modules consuming integration events implement the required inbox or dedupe pattern where enforceable
- synchronous cross-module reads target only `.PublicContracts` query contracts, stay out of `.Api` and `.Domain`, and remain read-only
- process managers and orchestration state live only in the owning module's Application and Infrastructure layers
- optional-module HTTP surfaces apply the shared module-state gate before feature logic executes
- typed module options stay out of Domain and Application code where enforceable
- reference modules bind typed module options through validated startup configuration when they surface runtime-operational settings
- machine-consumable endpoints declare a separate auth scheme and explicit allowlist when enabled
- feature real-time push uses the shared adapter and avoids feature-local websocket or SignalR clients
- shared runtime defaults do not silently register in-memory durability stores on the supported runtime path
- every command handler, query handler, and integration-event handler has either a corresponding test class or a governed handler-coverage inventory entry that names the covering integration test class or classes and justification
- no credential literals exist in committed configuration files (`docker-compose.yml`, `appsettings.*.json`)
- cross-module PublicContracts references per module stay within the governed cap
- shared read adapters declare explicit SharedReadPolicy with timeout and fallback behavior
- query and reader paths in module Infrastructure do not use EF Core eager loading via `.Include()` where projections are preferred

## Integration Gate

The integration test suite verifies:

- bootstrap reports registered modules and live states
- optional modules can be disabled and re-enabled without restart
- disabled modules stop serving new requests immediately
- dispatcher calls, module-owned event handlers, outbox dispatch, and workers respect module state outside HTTP through the shared execution gate
- core modules reject disable attempts
- concurrent module-state mutations are conflict-safe and do not produce split-brain live state
- enable failure rolls the module back to `Disabled` and records the failure
- module-state transitions are persisted in PostgreSQL, not local files
- module-state propagation across multiple app instances converges within the bounded staleness budget and fails closed on uncertainty
- drain coordination across multiple app instances waits for quiescence or records bounded recovery
- readiness and operational health distinguish `Disabled`, `Degraded`, and `Unhealthy`
- cookie auth flow works end to end
- antiforgery is enforced for state-changing requests
- malformed JSON and binding failures return normalized ProblemDetails
- non-mediator exceptions return normalized ProblemDetails through the host path
- OpenAPI includes module endpoints and problem responses
- outbox persistence and dispatch work across process restarts
- outbox retry and dead-letter behavior follow policy
- integration-event consumers are safe under duplicate delivery and replay
- compatibility-window dual-publish does not create duplicate downstream business outcomes for known consumers
- consumer inbox or dedupe state survives process restarts
- opted-in command idempotency survives retries and process restarts with stable outcomes
- process managers persist progress, resume after restart, and execute compensation or terminal failure rules correctly
- synchronous cross-module read paths stay read-only and respect timeout and fallback policy
- ASP.NET Core Identity security-stamp invalidation follows role changes, password resets, admin-triggered revocations, and account-state changes, and takes effect across instances
- machine-to-machine auth stays isolated from browser auth and enforces least privilege when enabled
- real-time push honors auth, role requirements, module state, and reconnect policy
- module-level bulkheads prevent one module's worker or dependency saturation from starving unrelated paths
- rolling deployment compatibility holds across previous and current app versions during expand-and-contract windows
- audit events are emitted for required security-sensitive and operational flows
- runtime configuration updates validate, persist, require step-up when marked sensitive, and become visible through the live module surface
- timezone and DST-sensitive behavior passes the required regression cases
- generated client contract tests consume the current API surface successfully
- the supported host and bootstrap path fail clearly when required shared-runtime database configuration is absent
- outbox circuit breaker trips after consecutive batch failures and recovers after backoff
- public list endpoints return paginated results with cursor-based continuation
- module state cache invalidates immediately on state transitions within the same instance
- dead-lettered outbox events are queryable through the admin API

## Frontend Gate

- `pnpm lint`
- `pnpm typecheck`
- `pnpm test`
- `pnpm test:e2e` on protected branches or on a documented branch policy
- components do not call `fetch` or alternative ad hoc HTTP clients directly
- remote data flows through RTK Query
- generated frontend scaffold unit-test anchors execute successfully in the supported temporary-workspace validator
- generated Playwright scaffold anchors execute successfully in the supported temporary-workspace validator
- no browser token storage in localStorage or sessionStorage
- browser telemetry uses the shared adapter instead of direct vendor SDK calls from feature code
- frontend consumes generated API contracts rather than hand-maintained transport mirrors
- browser-facing auth surfaces and route visibility use the role-based session model plus public-route visibility rather than permission arrays or legacy snapshot fields
- frontend feature manifests reconcile with modules that declare frontend surface
- feature routes are lazy-loaded from the module manifest and disabled modules do not eagerly fetch feature chunks
- each feature route is wrapped in a feature-level error boundary isolating runtime failures to the affected module
- browser real-time connections go through the shared adapter rather than feature-local clients
- normalized server-failure handling is exercised in unit or E2E coverage
- all feature forms use schema-driven validation (React Hook Form + Zod) with `aria-invalid` and `aria-describedby` on validated fields
- no inline submit-button-disable validation remains in form components

## Contract Gate

- OpenAPI generation succeeds
- generated TypeScript client output is refreshed and the repo is clean after generation
- every externally consumed HTTP endpoint belongs to an explicit version set
- breaking contract changes are detected by snapshot or client-generation diff checks
- breaking integration-event changes are detected by contract snapshots or schema diff checks
- shared query-contract consumer and provider compatibility tests pass across modules when those contracts are actually consumed cross-module
- deprecated public contracts carry owner, replacement, and removal-target metadata
- removal of deprecated public contracts is blocked before the declared compatibility window expires
- optional-module HTTP surfaces advertise canonical `503` ProblemDetails responses for disabled and disabling runtime states
- explicit replay upcasters, when defined, are current and pass retained-payload fixture tests
- problem response metadata remains present for all non-success API shapes

## Data Gate

- no pending EF model changes
- each module's migrations compile and apply cleanly
- production migrator smoke tests acquire the expected database lock
- mutable aggregates with concurrent writes exercise optimistic concurrency behavior
- migrations remain safe for rolling deployment and follow expand-and-contract sequencing
- destructive schema cleanup is blocked until the compatibility window closes
- module schemas remain isolated with no cross-module foreign keys; repeatable infrastructure table names are allowed only when each copy stays inside its owning schema
- EF Core modules support `--rollback-to` via DbMigrator for emergency schema reversion
- destructive contract migrations throw `NotSupportedException` from `Down()` and link a durable rollback assessment under `docs/operations/rollback/`; the supported recovery path is documented in [`MIGRATION_ROLLBACK.md`](MIGRATION_ROLLBACK.md)

## Security And Redaction Gate

- auth, antiforgery, and authorization failures use the canonical ProblemDetails codes
- browser telemetry redaction tests prove banned sensitive values are not emitted through the shared browser telemetry adapter
- platform admin mutation flows remain rate-limited and explicitly protected
- auth cookie settings remain explicit and secure by default
- shared Data Protection key storage works across multiple app instances on the default composed runtime
- same-origin remains the default and CORS stays disabled unless an approved ADR-backed allowlist is configured
- sensitive platform and admin browser mutations require recent-auth or equivalent step-up protection
- role revocation and admin-triggered session revocation bump the ASP.NET Core Identity security stamp and invalidate active cookies across instances
- machine-client auth remains audited, least-privilege, and isolated from browser cookies when enabled
- machine-authenticated requests are exempt from browser antiforgery validation
- login and step-up enforce explicit per-IP rate limiting, and password mutation enforces explicit per-authenticated-user rate limiting
- login, step-up, and password-mutation rate limiting each have explicit integration coverage
- `dotnet list package --vulnerable --include-transitive` reports zero advisories on every supported branch (CI gate; SDK-built-in, no third-party scanner required)
- CodeQL static analysis runs on every PR, push to `main`, and on a weekly schedule for `csharp` and `javascript-typescript` with the `security-and-quality` query pack
- Dependabot proposes grouped weekly updates for `nuget`, `github-actions`, `docker`, and `npm` so security patches reach the repo without manual sweep work
- `scripts/Test-SecretScan.ps1` runs a pinned, SHA-256-verified gitleaks release over the working tree and git history on every PR; any finding fails the `secret-scan` CI gate before build. Known-safe development placeholders live in `.gitleaks.toml`; silencing a real finding by expanding the allowlist is not supported — rotate the leaked value instead

## Minimum Merge Policy

Every PR must fail fast if a structural gate fails.

The canonical, ordered CI gate list is the manifest at [`governance/ci-gates.json`](../governance/ci-gates.json). Both the GitHub Actions workflow at [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) and the local pre-push runner at [`scripts/Invoke-LocalGates.ps1`](../scripts/Invoke-LocalGates.ps1) dispatch every gate through [`scripts/Invoke-CiGate.ps1`](../scripts/Invoke-CiGate.ps1) `-Id <id>`. Gates are added, removed, or reordered by editing the manifest — never by editing only one of the workflow or the local runner. The `LocalGateParityTests` architecture test fails if either side drifts from the manifest, and `Validate-Governance.ps1` enforces that every job's `needs:` clause in `.github/workflows/ci.yml` matches the manifest's `jobs` section exactly.

Local pre-push check:

```powershell
pwsh scripts/Invoke-LocalGates.ps1
```

Use `-List` to preview the plan, `-Only <id>[,<id>]` to re-run a single gate after a fix, `-From <id>` to resume a partial run, and `-IncludeSkipped` to run gates flagged `skipLocally` (heavyweight smokes that need a postgres service container or Playwright Chromium installed locally).

Required CI stages (consult the manifest for current order and command bodies):

1. Spec consistency and scaffold smoke (`spec-governance`)
2. Dependency allowlist (`dependency-policy`)
3. Secret scan (`secret-scan`)
4. Restore and build (`build`, `format-check`, `dbmigrator-smoke`)
5. Dependency vulnerability scan (`dependency-vulnerability-scan`)
6. Backend unit tests (`backend-unit-tests`)
7. Architecture tests (`architecture-tests`)
8. Integration tests (`integration-tests`)
9. ApiHost missing-configuration smoke (`apihost-missing-configuration-smoke`)
10. ApiHost missing-migrations smoke (`apihost-missing-migrations-smoke`)
11. ApiHost bootstrap smoke (`apihost-bootstrap-smoke`)
12. Contract generation and compatibility checks (`contract-generation-and-compatibility`)
13. Frontend lint, typecheck, and production build (`frontend-lint`, `frontend-typecheck`, `frontend-build`)
14. Frontend unit tests (`frontend-unit-tests`)
15. Playwright end-to-end tests (`frontend-e2e`) according to branch policy

No PR merges on red or skipped required gates.
