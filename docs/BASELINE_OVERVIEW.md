<!-- doc-tier: maintained-narrative -->
# Baseline Overview

This document explains the posture of `dotnet-modulith-baseline` in plain language.

It is intentionally shorter and less rule-dense than [`BLUEPRINT.md`](BLUEPRINT.md), but it should help adopters understand what this repository is designed to provide, what it deliberately does not optimize for, and what expectations come with adopting it.

For the governed architecture, treat these documents as the source of truth:

- [`BLUEPRINT.md`](BLUEPRINT.md)
- [`QUALITY_GATES.md`](QUALITY_GATES.md)
- [`ADD_MODULE.md`](ADD_MODULE.md)
- [`RULE_TO_GATE_CATALOG.md`](RULE_TO_GATE_CATALOG.md)
- [`ADOPT.md`](ADOPT.md)

## Baseline summary

This repository is a **governed, production-leaning modulith baseline**.

It is intended for teams that want a modular monolith whose structural rules remain enforceable as more modules, contributors, and operational requirements are added.

It is not intended to be the smallest possible sample, the most flexible possible template, or the lowest-ceremony first-day demo.

## First-time evaluation path

For a cold-start OSS evaluation, use the full Docker composed path from [`../README.md`](../README.md) first:

1. copy `.env.example` to `.env`
2. run `docker compose up --build`
3. open `http://localhost:8080`
4. sign in with the default Development-only bootstrap credential from `.env.example` (`admin` / `LocalOnly!123`)

That path brings up PostgreSQL, DbMigrator, and ApiHost together and is the least ambiguous way to confirm the baseline works as-shipped.

The local backend path is still supported, but it is more manual: you must provide `ConnectionStrings__BaselineDatabase`, configure a Development-only seeded admin password, and either run `pnpm dev` or build `web/dist` before expecting the browser shell. For first impressions, prefer the full Docker path.

## What this baseline provides

### Governed architecture, not just folder conventions

This repository treats architecture as something that should be:

- documented up front
- scaffolded consistently
- enforced by tests and validation scripts
- kept synchronized as the repository evolves

That is why the repository includes a blueprint, a rule-to-gate catalog, ADRs, scaffold smoke tests, architecture tests, integration tests, frontend validation, CI shape checks, and explicit security-oriented workflow guardrails such as format enforcement, dependency-vulnerability scanning, and CodeQL-backed analysis.

### Thin host, module-owned implementation boundaries

The intended backend shape is:

- `ApiHost` composes the application and stays feature-thin
- each module owns its API, application, domain, infrastructure, and public contracts
- cross-module communication goes through governed public contracts only
- persistence, workers, migrations, and external adapters remain module-owned infrastructure concerns

This keeps feature behavior out of the host and makes ownership explicit.

### Repository-owned platform seams where the baseline needs control

This baseline deliberately owns several core seams directly, including:

- a custom dispatcher instead of MediatR
- a baseline-owned outbox and inbox posture instead of a heavier messaging framework
- baseline-owned runtime module activation infrastructure
- baseline-owned scaffold and governance validation scripts

This gives the repository tighter control over behavior, ordering, conventions, and drift resistance. It also makes the repository more platform-like than a minimal sample.

### Production-leaning defaults for the supported runtime path

The repository optimizes for the industrialized end-state described in [`BLUEPRINT.md`](BLUEPRINT.md), including:

- minimal APIs only
- cookie authentication and antiforgery for browser clients
- same-origin browser posture by default
- generated browser contracts
- PostgreSQL-backed durability on the supported composed runtime
- runtime module enable and disable
- durable outbox, inbox or dedupe, idempotency, and audit patterns
- per-user rate limiting on authentication and password-mutation endpoints
- per-request timeout enforcement in the dispatcher pipeline
- interactive API documentation via Scalar
- feature-level React error boundaries isolating runtime failures per module
- testable timezone- and DST-safe behavior

This does **not** mean every shipped implementation is the final production answer for every adopter. It means the baseline prefers durable structural defaults over convenience-first shortcuts.

That production-leaning posture now also extends beyond code shape into workflow and operations: deployment guidance, migration-rollback guidance, and default telemetry-collector wiring are part of the supported story rather than afterthoughts.

### Intentional reference modules

The checked-in modules are deliberate teaching slices, not accidental examples.

They cover different parts of the baseline:

- `Platform`: operational control plane, bootstrap manifest, module state, health, audit
- `Identity`: production-grade ASP.NET Core Identity with cookie auth, role-based authorization, machine-auth scheme, and security-stamp session invalidation
- `SampleFeature`: smallest scaffold-first reference slice
- `Blog`: first production-grade EF-first teaching slice
- `KnowledgeBase`: richer content, runtime settings, idempotent publication, public-facing flow
- `Admin`: advanced cross-module projections, replay-safe consumption, recovery workers, realtime

Two of these modules are core (`Platform`, `Identity`). The other four are teaching modules that adopters may keep temporarily, study, or remove during adoption depending on the chosen fork shape.

These modules are reference material. They are not the supported mechanism for creating new modules. The supported structural path is the scaffold in [`../scripts/New-Module.ps1`](../scripts/New-Module.ps1).

For the module-by-module view, see [`MODULE_GUIDE.md`](MODULE_GUIDE.md).

## What this baseline does not provide

### It is not a minimal tutorial repository

Teams looking for a tiny repository that demonstrates modular folders, a few handlers, and a few endpoints should expect this baseline to feel heavy.

The repository includes governance assets, runtime module state infrastructure, contract lifecycle rules, scaffold regression checks, frontend feature manifests, and a substantial validation wall by design.

### It is not a generic bring-any-style baseline

This repository makes explicit architectural choices.

It is not trying to support all of these equally:

- controllers and minimal APIs
- MediatR and custom dispatch
- JWT SPA auth and cookie auth
- same-origin and arbitrary cross-origin defaults
- PostgreSQL and every other relational database
- scaffold-first and copy-an-existing-module workflows

The posture is to choose a supported default and enforce it rather than keep every alternative equally open.

### It is not provider-agnostic on the supported runtime path

The supported composed runtime is PostgreSQL-backed.

That posture affects more than the EF Core provider choice. The baseline relies on PostgreSQL-specific patterns for:

- schema-per-module isolation
- advisory-lock migration coordination
- durable shared-runtime storage
- outbox and inbox operational behavior
- Testcontainers-backed integration tests

Adopters can replace those choices, but doing so is a deliberate infrastructure rewrite, not a configuration toggle.

### It is not a silent fallback runtime

This baseline does not treat missing shared-runtime durability as a convenience mode.

If `ConnectionStrings:BaselineDatabase` is missing on the supported runtime path, startup should fail clearly. The repository does not treat an in-memory fallback as an equivalent local-development mode for the composed runtime.

### Identity is production-grade, not a stub

The `Identity` module is a **production-grade implementation** built on standard ASP.NET Core Identity, cookie authentication, EF Core, and role-based authorization.

Its role is to:

- provide a supported production authentication and authorization answer behind the module boundary
- route every Identity use case through the module dispatcher instead of `MapIdentityApi`
- enforce role-based authorization via `RoleRequirement` in the dispatcher pipeline
- drive session invalidation through ASP.NET Core Identity's security stamp
- keep machine-client lifecycle persisted with hashed secrets and role assignments

Teams that require a different identity provider may replace `Identity.Infrastructure` with their own implementation, but the baseline ships a working production answer rather than a stub. Seeded admin and seeded machine credentials are Development/Testing only; any other environment fails fast at startup.

See [`adr/ADR-IDENTITY-POSTURE.md`](adr/ADR-IDENTITY-POSTURE.md) for the governing decision.

### It is not a copy-paste module workflow

New modules are not supposed to be created by cloning `SampleFeature`, `Blog`, `KnowledgeBase`, or `Admin`.

Those modules exist to teach the supported seams after scaffolding. They are references, not templates.

The intended workflow is:

1. decide the module inputs described in [`ADD_MODULE.md`](ADD_MODULE.md)
2. scaffold the module through the supported script
3. extend the scaffolded shell through normal implementation work
4. use the reference modules only when a concrete example is needed for a specific seam

## What adopters should expect

Teams adopting this baseline should expect the following posture unless they intentionally replace it:

- architecture is governed, not informal
- structural changes go through the scaffold and the quality wall
- modules own their boundaries explicitly
- browser authentication is cookie-based and antiforgery-protected
- browser remote data flows through generated contracts and RTK Query
- integration tests expect PostgreSQL and Docker or an equivalent container runtime
- reference modules remain teaching assets even if some are later removed from a fork

Teams should also expect that several choices are intentionally optimized for the long-term target shape rather than for the smallest first commit.

For adopters planning to fork the repo into a product base, the safest next step is [`ADOPT.md`](ADOPT.md). It documents one supported adoption workflow with two entry points: use `pwsh ./scripts/Start-Adoption.ps1` for first-time human adoption, or use the config-first path there when the inputs are already resolved or the run is agent-driven.

## Best fit

This baseline is a strong fit when a team wants:

- a serious modular-monolith foundation
- strong architectural boundaries from day one
- executable drift resistance
- a scaffold-first path for adding bounded contexts
- a backend and frontend shape that remain aligned over time
- reference modules that teach the intended seams

## Poor fit

This baseline is a poor fit when a team wants:

- the smallest possible sample
- maximum architectural flexibility
- a repository that stays mostly convention-based and lightly enforced
- a baseline that treats database, auth, transport, and composition choices as interchangeable
- a low-ceremony proof of concept with minimal operational posture

## Practical nuance

The most accurate way to think about this repository is:

> It is a production-leaning baseline for teams that want a modular monolith to stay structurally honest over time.

It is **not** a universal .NET template, and it is **not** claiming that every included implementation is the forever answer for every adopter.

The repository aims to provide:

- a governed shape
- a strong default extension path
- durable operational seams
- reference modules that teach the intended architecture

Adopters can then replace selected infrastructure behind those seams when their own product requirements demand it.

## Related reading

- Start with [`../README.md`](../README.md) for bootstrap and workflow basics.
- Read [`ADOPT.md`](ADOPT.md) if you plan to fork and rename the baseline.
- Read [`BASELINE_DECLARATION.md`](BASELINE_DECLARATION.md) for the repository's current maintenance and completeness posture.
- Read [`MODULE_GUIDE.md`](MODULE_GUIDE.md) for the module-by-module view.
- Read [`ARCHITECTURAL_RISKS.md`](ARCHITECTURAL_RISKS.md) for the current architecture critique and prioritized risk view.
- Read [`REFERENCE_MODULE_TEACHING_MAP.md`](REFERENCE_MODULE_TEACHING_MAP.md) if you want to locate specific seams quickly.
- Read [`BLUEPRINT.md`](BLUEPRINT.md) if you need the full governed architecture.
