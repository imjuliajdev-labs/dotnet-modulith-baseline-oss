<!-- doc-tier: maintained-narrative -->
# Module Guide

This guide describes the role of each checked-in module in the repository, the kinds of seams it is intended to teach, and the situations in which it is the best reference.

Use this document when you need a module-by-module explanation of the baseline without reading every module README first.

This guide complements, but does not replace:

- [`REFERENCE_MODULE_TEACHING_MAP.md`](REFERENCE_MODULE_TEACHING_MAP.md)
- the module READMEs under `src/Modules/*/README.md`
- [`ADD_MODULE.md`](ADD_MODULE.md)
- [`ADOPT.md`](ADOPT.md)

## First-time OSS walkthrough

If you are evaluating the running OSS rather than extending the code immediately, use the full Docker composed path in [`../README.md`](../README.md) first. On the default checked-in Development path, sign in as `admin` with password `LocalOnly!123`.

After sign-in, use this rule of thumb:

- start with `Platform` to understand what the baseline bootstraps and which modules are live
- open `SampleFeature` to see the smallest end-to-end governed feature
- open `Blog` to see the default EF-first business-module shape
- open `KnowledgeBase` to see runtime settings and richer content flow
- open `Admin` last to inspect advanced cross-module projection, machine-endpoint, recovery, and realtime behavior

If you plan to fork the repository after evaluation, keep `Platform` and `Identity`, then decide whether the four teaching modules should remain temporarily as live references or be removed immediately through the config-first adoption flow in [`ADOPT.md`](ADOPT.md).

## Reference usage rule

The checked-in modules are **reference material, not scaffolding**.

New modules should be added through [`../scripts/New-Module.ps1`](../scripts/New-Module.ps1), then extended through normal implementation work. The existing modules demonstrate the supported seams after scaffolding. They are not the supported mechanism for creating new modules and should not be cloned as a starting point.

Two modules are core and should be treated differently from the rest:

- `Platform`
- `Identity`

The remaining checked-in modules are teaching slices with different levels of realism and complexity.

## Module summary

| Module | Core | Frontend surface | Primary persistence style | Primary role |
| --- | --- | --- | --- | --- |
| `Platform` | Yes | Yes | EF-backed operational store | control plane and runtime backbone |
| `Identity` | Yes | No module-owned feature manifest | EF + ASP.NET Core Identity | production-grade authentication, session, and role-based authorization |
| `SampleFeature` | No | Yes | EF Core + migrations | smallest end-to-end governed feature reference |
| `Blog` | No | Yes | EF Core + migrations | default relational feature reference |
| `KnowledgeBase` | No | Yes | specialized PostgreSQL store | richer content and runtime-settings reference |
| `Admin` | No | Yes | inbox and process-manager stores | advanced integration, projection, and recovery reference |

## Platform

### Primary role

`Platform` is the repository's operational control plane.

If you are clicking through the app for the first time, this is the module that explains the shell itself: it tells you which modules are enabled, what the host believes is live, and which operational surfaces the baseline treats as first-class.

It owns the surfaces and state behind:

- bootstrap manifest
- module state listing
- module enable and disable
- operational health
- audit event reads
- machine bootstrap reads
- durable runtime module state

It is a core module and cannot be disabled.

### What it should not be used as

`Platform` is not a business feature module and it is not the model for ordinary product modules.

Its purpose is to hold platform concerns that the rest of the baseline depends on.

### Why it matters

`Platform` is one of the main modules that makes the repository more than a conventional modular-monolith sample. It is where the baseline proves that runtime activation, operational visibility, and audit-backed state transitions are first-class concerns.

### Use it as a reference when

Study `Platform` when you need examples for:

- runtime module activation
- durable module state transitions
- operational endpoints
- bootstrap manifest composition
- audit-backed platform operations

## Identity

### Primary role

`Identity` is the production-grade authentication, session, account, role, and machine-client module. It is built on standard ASP.NET Core Identity, cookie authentication, and EF Core persistence.

It owns:

- browser sign-in, sign-out, and antiforgery token issuance
- recent-auth step-up for sensitive mutations
- current-actor reads and preferred time zone
- user administration (create, disable, enable, unlock)
- role administration (assign, replace, remove)
- password change (self) and password reset (admin)
- admin-triggered session revocation via ASP.NET Core Identity's security stamp
- machine-client lifecycle (create, rotate, disable, reactivate, revoke) with hashed secrets
- separate machine authentication via the `Machine` scheme

It is a core module and cannot be disabled.

### Roles

Three roles are seeded at module initialization and are the first-class authorization primitive:

| Role      | Use                                                                                                                    |
| --------- | ---------------------------------------------------------------------------------------------------------------------- |
| `Admin`   | Full administrative authority. Required for user administration, role administration, machine-client administration, and platform mutations. |
| `User`    | Standard authenticated human user. Default role assigned to new accounts.                                              |
| `Machine` | Persisted machine client. Assigned to machine principals. Cannot sign in through the browser cookie flow.              |

Commands and queries declare `RoleRequirement` entries in their `AuthorizationRequirements` collection; the dispatcher enforces them centrally.

### Adding a new role or changing assignments

To add a new role:

1. Add the constant to `IdentityRoles` in `Identity.Infrastructure.Authentication`.
2. Seed the role in `IdentityDatabaseMigration` alongside the existing `Admin` / `User` / `Machine` seeds using `RoleManager<IdentityRole>`.
3. Update `IdentityAccountSupport.IsKnownRole` so validation accepts the new name.
4. Reference the role through `RoleRequirement` on any command or query that should be gated by it.

To change an existing user's role assignments, use `PUT /api/v1/identity/users/{actorId}/roles`. The self-demotion guard in `SetIdentityUserRolesCommand` prevents an admin from removing their own `Admin` role.

### Production deployment and bootstrap credentials

Seeded admin and seeded machine credentials are **Development and Testing only**. Any other environment fails fast at startup if they are configured. There is no override flag.

The production bootstrap procedure is:

1. In a clean deployment, sign in once using a Development-only seeded admin credential (local only) and mint a real admin user through the user administration endpoints, or provision the first admin directly via a one-off migration path owned by the deploying team.
2. Remove all `Modules:Identity:SeededAdmin:*` and `Modules:Identity:SeededMachine:*` configuration from the non-development environment before the first deploy.
3. Use the admin user administration endpoints to create additional operators, assign roles, rotate passwords, and revoke sessions as needed.

To configure a seeded machine client for local development or test runs, set:

- `Modules:Identity:SeededMachine:ApiKey` — the `{clientId}:{secret}` bootstrap value
- `Modules:Identity:SeededMachine:Roles` — the role list (typically `Machine`, optionally `Admin`)

### What it should not be used as

`Identity` is not the best template for a normal optional feature module. It is a core module with its own posture.

### Why it matters

`Identity` anchors several repo-wide architectural decisions:

- cookie auth over JWT for browser clients
- antiforgery-protected browser mutations
- separate machine authentication
- recent-auth enforcement
- role-based authorization as the dispatcher-level primitive
- security-stamp-driven session invalidation across instances

### Use it as a reference when

Study `Identity` when you need examples for:

- ASP.NET Core Identity with cookie authentication in a same-origin app
- role-based authorization through the dispatcher
- recent-auth step-up
- explicit admin-triggered session revocation via `UpdateSecurityStampAsync`
- machine-client management with hashed secrets and lifecycle
- routing every use case through the module dispatcher instead of `MapIdentityApi`

Endpoint registrations live in per-seam files under `Identity.Api/Endpoints/` (`SignInEndpoints`, `CurrentUserEndpoints`, `AdminUserEndpoints`, `MachineClientEndpoints`). `IdentityModule.MapEndpoints` is a thin list of `Map{Seam}Endpoints()` calls, mirroring `BlogModule`.

See [`adr/ADR-IDENTITY-POSTURE.md`](adr/ADR-IDENTITY-POSTURE.md) for the governing decision.

## SampleFeature

### Primary role

`SampleFeature` is the smallest end-to-end governed EF-backed feature module in the repository.

For first-time adopters, this is usually the quickest feature to inspect after `Platform` because it shows the baseline shape without the richer editorial and integration complexity of `Blog`, `KnowledgeBase`, or `Admin`.

It demonstrates:

- a minimal `IApiModule` descriptor and endpoint surface
- a small but real domain value object
- antiforgery-protected writes
- `Idempotency-Key` enforcement
- EF Core persistence with a module-owned DbContext and migrations
- module-owned outbox publication
- a public integration event
- a lazy frontend feature

It is optional and can be disabled at runtime.

`SampleFeature` is fully self-contained. It does not depend on any other teaching module, so it can be removed without breaking `Blog`, `KnowledgeBase`, or any other simpler reference. Only `Admin` consumes its public integration event and would need its consumer cleanup if `SampleFeature` is removed.

### What it should not be used as

`SampleFeature` is not the supported scaffolding mechanism. It exists only as reference material.

It is also not the most realistic production slice in the repository. Its role is to be the smallest concrete module that still proves the governed shape.

### Why it matters

`SampleFeature` is the clearest small-scale proof of what a complete governed EF-backed feature looks like after scaffolding and implementation. Together with `Blog`, it reinforces the EF-first persistence path that the docs and scaffold describe as the default.

### Use it as a reference when

Study `SampleFeature` when you need examples for:

- the smallest EF-backed feature implementation in the repository
- an idempotent write plus outbox pattern on an EF module
- a simple optional module with frontend discovery
- a self-contained teaching slice with no cross-module reads

## Blog

### Primary role

`Blog` is the first production-grade EF-first teaching slice.

For adopters asking “what does a realistic default business module look like on this baseline?”, `Blog` is usually the best answer.

It demonstrates:

- a real aggregate
- EF Core persistence and migrations
- taxonomy
- editorial workflows
- operator-managed settings
- time-zone-aware scheduling
- due-work background processing
- module-owned outbox publication
- public query surfaces
- a matching frontend feature

It is optional and can be disabled at runtime.

### What it should not be used as

`Blog` is not merely a CRUD sample and it is not the advanced integration and recovery reference represented by `Admin`.

It is the closest checked-in module to the default model for a normal relational business feature.

### Why it matters

For adopters building a real EF-backed bounded context on this baseline, `Blog` is usually the most representative module to study first.

### Use it as a reference when

Study `Blog` when you need examples for:

- the default EF-first module model
- real aggregate plus migration patterns
- scheduling and background workers
- public plus managed workflows in one bounded module
- a realistic feature reference without the complexity of the advanced integration modules

## KnowledgeBase

### Primary role

`KnowledgeBase` is the richer content and runtime-settings module.

It is the best next stop after `Blog` when you want to see how the baseline handles operator-managed settings, richer public content flows, and more specialized persistence choices.

It demonstrates:

- public reads plus operator-managed writes
- typed runtime settings
- validated startup binding
- audited runtime settings updates
- publish workflows with idempotency
- richer public contract evolution
- module-owned public integration events
- a browser-facing content feature
- rich domain behavior methods on the aggregate (factory methods, state transitions, validation)

It is optional and can be disabled at runtime.

### What it should not be used as

`KnowledgeBase` is not the default persistence baseline for new modules.

It uses specialized PostgreSQL store patterns where EF is not the best fit. That makes it a valuable reference for specialized cases, but not the first model to copy for ordinary relational feature work.

### Why it matters

`KnowledgeBase` is the strongest checked-in example for settings governance, richer content lifecycle behavior, and idempotent publication in a public-facing feature.

### Use it as a reference when

Study `KnowledgeBase` when you need examples for:

- runtime settings governance
- public and management APIs in one module
- idempotent publish behavior
- richer contract evolution examples
- more advanced frontend conflict and settings flows

## Admin

### Primary role

`Admin` is the advanced integration, projection, and recovery module.

This module makes the most sense once you already understand the simpler teaching slices. It is intentionally not the first module new adopters should model their own product modules after.

It demonstrates:

- machine-consumable endpoints
- public read models
- cross-module event consumption from `SampleFeature` and `KnowledgeBase`
- dual-version contract migration with V1 legacy bridge and V2 consumer coexistence
- replay-safe inbox patterns
- process-manager orchestration
- recovery worker behavior
- shared realtime push
- a bounded shared read into `KnowledgeBase`'s public query contract

It is optional and can be disabled at runtime.

`Admin` is the only teaching module allowed to depend backward into other teaching modules. That backward reach is intentional: it lets `Admin` teach how an advanced module consumes simpler modules' public surface without polluting the simpler modules with knowledge of `Admin`. Removing `SampleFeature` or `KnowledgeBase` requires deleting the matching consumer files in `Admin` (and only there).

### What it should not be used as

`Admin` is not the right first module to emulate and it is not merely a thin administrative UI.

Its role is to teach advanced seams such as durable projection workflows, replay-safe consumer handling, process-manager state, recovery under interruption, machine endpoint posture, and realtime integration.

### Why it matters

`Admin` is where the repository proves that the advanced parts of the baseline are implemented, not merely described.

### Use it as a reference when

Study `Admin` when you need examples for:

- inbox or dedupe consumer patterns
- projections from another module's events
- process managers
- recovery workers
- machine endpoints
- realtime fanout through shared infrastructure

## Recommended study order

For contributors who are new to the repository, the most approachable study order is:

1. `SampleFeature`
2. `Blog`
3. `KnowledgeBase`
4. `Admin`
5. `Identity`
6. `Platform`

That sequence moves from the smallest feature slice to the most platform-specific behavior.

## Which module to study first

Use the following rule of thumb:

- start with `SampleFeature` for the smallest feature example
- start with `Blog` for the best default reference for a real business module
- start with `KnowledgeBase` for settings and public-content lifecycle patterns
- start with `Admin` for advanced event consumption, projection, recovery, or realtime patterns
- study `Identity` for auth and session architecture
- study `Platform` for runtime module control and operational state

## Forking guidance

When adopting this repository into a product base:

- keep `Platform`
- keep `Identity`
- treat the remaining modules as teaching material that may be kept temporarily or removed later
- make that keep/remove decision explicitly through the config-first adoption workflow in [`ADOPT.md`](ADOPT.md) before mutating the repo

The teaching graph is directional. `SampleFeature`, `Blog`, and `KnowledgeBase` are all standalone with respect to each other, so any one of them can be removed without breaking the others. Only `Admin` reaches backward into the simpler modules; removing one of them requires deleting the matching consumer in `Admin`.

In practice:

- `Blog` is the most useful module to retain if one realistic EF-backed reference should remain longest
- `SampleFeature` is the easiest smallest EF reference to keep while learning the baseline
- `KnowledgeBase` is useful when the product needs richer settings and content flows early
- `Admin` is useful when the product needs advanced integration patterns early; remove it last because nothing else depends on it

For the supported adoption path, see [`ADOPT.md`](ADOPT.md).

## Final takeaway

The modules are intentionally differentiated.

- `Platform` and `Identity` establish the baseline runtime and authentication posture.
- `SampleFeature` shows the smallest credible governed EF-backed feature.
- `Blog` shows the default relational feature path at production scale.
- `KnowledgeBase` shows richer settings and content patterns.
- `Admin` shows advanced integration and recovery patterns.

The intended usage pattern is:

1. add new modules through the supported scaffold workflow
2. use the checked-in modules only as focused references
3. prefer the smallest module that demonstrates the seam you need
