# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-04-17

Initial public release of `dotnet-modulith-baseline` — a production-grade .NET 10 modular monolith baseline designed to resist architectural drift.

### Added

#### Architecture

- Governed modular monolith with **39 numbered non-negotiable rules** (BP-001 — BP-039), each backed by executable enforcement through tests, linters, script validation, or governed waiver handling.
- **Six checked-in modules**: two core modules (`Platform`, `Identity`) plus four teaching modules (`Admin`, `SampleFeature`, `KnowledgeBase`, `Blog`).
- Custom dispatcher with explicit 8-stage request pipeline.
- Cookie-based browser authentication via ASP.NET Core Identity (`__Host-` cookies, antiforgery, sliding 8h, Data Protection key store for cross-instance cookie invalidation).
- Separate machine-client authentication with hashed secrets, rotation, and revocation.
- Role-based authorization (`Admin`, `User`, `Machine`) enforced through the dispatcher pipeline.
- Runtime module enable/disable with drain-safe state machine.
- PostgreSQL schema-per-module with advisory-lock-based migration runner.
- Integration event outbox/inbox with at-least-once delivery, replay safety, deterministic failure classification (typed exceptions for unresolvable types, deserialization mismatches, id mismatches), signal-status observability, and dedicated health checks.
- Durable command idempotency with opt-in policy.
- Process manager pattern with recovery worker.
- Cross-module projections with replay-safe inbox and compatibility tests.
- Module-owned rate limiter contributors (`IModuleRateLimiterContributor`) so ApiHost never imports module-API namespaces.
- Typed inbox consumer names (`InboxConsumerName` value type) with single production factory.
- Canonical Postgres outbox/inbox helpers shared across all modules (no bespoke wrappers, no NoOp store fallbacks in production).

#### Frontend

- React 19 / Vite / TypeScript with RTK Query.
- OpenAPI-to-TypeScript contract generation pipeline.
- Module-aware lazy-loading and feature registry driven by the Platform bootstrap manifest.
- Schema-driven forms (`react-hook-form` + `zod`) with enforced aria wiring (BP-037).
- Shared SignalR real-time adapter with per-frame auth.
- Tightened CORS with explicit, wildcard-free allow-lists (`FrontendCorsOptions`, `ValidateOnStart`-guarded).
- App-shell auth separated from feature manifests (`web/src/shell/auth/`); `web/src/features/` holds only BP-017 feature manifests.

#### Governance & quality gates

- Single canonical CI-gate manifest at `governance/ci-gates.json`, dispatched through `scripts/Invoke-CiGate.ps1`.
- Local pre-push runner `scripts/Invoke-LocalGates.ps1` with `LocalGateParityTests` enforcing local ↔ CI agreement on every gate id, command, and topological order.
- Architecture and integration test suites covering cross-module reference caps, BuildingBlocks growth budgets, handler-coverage dispatch verification, csproj canonical shape, query/projection separation, schema-driven forms, cursor pagination, frontend e2e mocking bans, time-strategy compliance, and chaos / failure-mode scenarios against the real outbox dispatcher, real Postgres driver, and real shared-read adapters.
- Operator runbooks for outbox dead-letter classification and signal-degraded recovery.
- Waiver model with expiry enforcement and executable validation (`WaiverValidationTests`).
- Per-project file/LOC growth budget for `BuildingBlocks.*` projects (`governance/buildingblocks-budget.json`).
- Banned-symbols enforcement (`BannedSymbols.txt`) covering `DateTime.Today`, `Environment.TickCount*`, `Stopwatch.GetTimestamp`, and other server-local-time / direct-wall-clock hazards.
- Workflow-configuration freshness guardrail preventing reintroduction of removed permission-era keys.
- Documentation reference integrity guardrail.

#### Tooling

- Scaffold-first module creation via `scripts/New-Module.ps1` with spec-driven generation and EF Core persistence support.
- Adoption workflow via `scripts/Adopt-Baseline.ps1` for forking and renaming the baseline (Docker file renames, wildcard module references, frontend manifests, schema names).
- Docker Compose setup (PostgreSQL + DbMigrator + ApiHost).
- GitHub Actions CI with a manifest-backed gate set covering spec-governance, dependency-policy, secret-scan, dependency-vulnerability scanning, contract generation, frontend lint/typecheck/build/unit/e2e, and architecture/integration test suites.

#### Documentation

- [README](./README.md) — quickstart and onboarding.
- [BLUEPRINT](./docs/BLUEPRINT.md) — the BP-001 through BP-039 non-negotiable rule set.
- [QUALITY_GATES](./docs/QUALITY_GATES.md) — gate catalog and enforcement model.
- [RULE_TO_GATE_CATALOG](./docs/RULE_TO_GATE_CATALOG.md) — every BP-NNN mapped to its executable enforcement.
- [ADOPT](./docs/ADOPT.md) — fork-and-rename guide.
- [BASELINE_OVERVIEW](./docs/BASELINE_OVERVIEW.md) — plain-language posture.
- [MODULE_GUIDE](./docs/MODULE_GUIDE.md) — module roles, usage, and study recommendations.
- [REFERENCE_MODULE_TEACHING_MAP](./docs/REFERENCE_MODULE_TEACHING_MAP.md) — pattern-to-module index.
- [EF_MODULE_PERSISTENCE_GUIDE](./docs/EF_MODULE_PERSISTENCE_GUIDE.md) — read/write helper split, migration posture.
- [TELEMETRY](./docs/TELEMETRY.md) and [SECRET_MANAGEMENT](./docs/SECRET_MANAGEMENT.md).
- 14 ADRs under [docs/adr/](./docs/adr/).
- Governed documentation set: AGENTS.md (gated non-negotiables vs. contributor conventions), ARCHITECTURAL_RISKS, FRONTEND_GOVERNANCE.
- OSS files: LICENSE (MIT), CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, SUPPORT.

[1.0.0]: https://github.com/imjuliajdev-labs/dotnet-modulith-baseline/releases/tag/v1.0.0
