# AGENTS.md

Entry point for AI coding agents and new contributors. Read this file first, then the governed documentation set below, before proposing or making any change in this repo.

## Read Order

This repo is governed by an explicit architecture. Do not propose changes before reading the governing documents in this order:

1. [`docs/BLUEPRINT.md`](docs/BLUEPRINT.md) — target architecture, non-negotiables, technology baseline, and module model.
2. [`docs/QUALITY_GATES.md`](docs/QUALITY_GATES.md) — how each rule is enforced, waiver policy, and what "done" means for a change.
3. [`docs/ADD_MODULE.md`](docs/ADD_MODULE.md) — the only supported workflow for adding a module.
4. [`docs/RULE_TO_GATE_CATALOG.md`](docs/RULE_TO_GATE_CATALOG.md) — the versioned index from each non-negotiable to its primary enforcement artifact.
5. [`docs/adr/`](docs/adr/) — decisions that changed a foundational default. Skim the index to confirm whether the area you are touching has an ADR.

The scaffold under `templates/` and `scripts/` is the only supported path for structural changes. Manual structural assembly is out of scope.

## Module Creation Guidance For Agents

When the user asks to add a new module or expand the baseline with a new bounded context:

- Prefer the shared repo-owned prompt at `prompts/add-governed-module.md`.
- Use `scripts/New-Module.ps1` with a module spec as the structural starting point.
- Do not clone, rename, or copy `SampleFeature` to create a new module.
- Do not clone, rename, or copy `KnowledgeBase` to create a new module.
- Treat `SampleFeature` as the smallest scaffold-first EF reference slice that proves the add-module workflow and one minimal filled-in path.
- Treat `Blog` as the first production-grade EF-first teaching slice for a real aggregate, migration, taxonomy, scheduling, module-owned outbox publication, and frontend editorial workflow.
- Treat `KnowledgeBase` as a richer reference for runtime settings and content workflows, not as the default persistence pattern.
- For module-owned relational persistence, prefer the EF Core DbContext patterns in `Platform.Infrastructure.Persistence` as the primary teaching reference.
- The scaffold now emits a default EF Core persistence shell and baseline entity-configuration example under each module's `.Infrastructure/Persistence`; extend that generated shell before reaching for other module implementations.
- When a module declares operator-managed settings, model them in the module spec as typed setting definitions rather than a bare reload-safe name list.
- When a module is expected to take cross-module `*.PublicContracts` dependencies, declare those module names explicitly in `crossModulePublicContractDependencies` and run `pwsh scripts/Report-ModuleDependencies.ps1` before scaffolding so BP-033 headroom is visible before the reference is introduced.
- Use the existing modules as reference material after scaffolding, not as the scaffolding mechanism.
- If the generated shell is missing a required structural capability, fix the scaffold or surface the gap. Do not hand-assemble the missing structure in the new module.
- Before running the scaffold, make the required module-spec decisions explicit per `docs/ADD_MODULE.md`.

## Fork And Adoption Guidance For Agents

When the user asks whether this baseline is ready to fork, or asks to adopt it into a new product base:

- Prefer the shared repo-owned prompt at `prompts/adopt-governed-baseline.md`; it follows the same supported workflow documented in `docs/ADOPT.md`, not a parallel path.
- Read [`docs/ADOPT.md`](docs/ADOPT.md) before proposing changes.
- Treat adoption as one supported workflow with two entry points:
  - for first-time human adopters with unresolved inputs, prefer the guided wrapper `scripts/Start-Adoption.ps1`
  - for agent-driven or already-decided adoption runs, prefer `templates/adoption/adopt-spec.example.json` plus `scripts/Adopt-Baseline.ps1`
- When you use `scripts/Start-Adoption.ps1`, remember that it gathers the target folder, optional target remote, human-facing application name, technical slug, module-retention choices, and validation profile, points the adopter to `docs/MODULE_GUIDE.md` and `docs/ADOPT.md`, writes `adopt-spec.json` into the target repo, runs the dry run first, and only applies after explicit confirmation.
- When you use `scripts/Adopt-Baseline.ps1`, use `-DryRun` first so the human-facing application name, technical slug, and module-retention decisions are explicit before mutation.
- All four `modulePreset` values are supported in apply mode, including `custom` with an explicit `keepModules` array; `Platform` and `Identity` must always be in the resolved keep set.
- Use the checklist in `docs/ADOPT.md` as the detailed reference for required identity surfaces and teaching-module dependencies, even when the script is doing the apply work.
- Adoption validation profiles dispatch through the canonical gate runner (`scripts/Invoke-LocalGates.ps1` and `scripts/Invoke-CiGate.ps1 -Id <id>`); do not hand-roll raw `dotnet test` or `pnpm` command lists when the governed gate wrappers already cover the path.
- Do not invent ad hoc rename or cleanup flows when the documented adoption workflow already covers the supported path.

## Working Rules For Agents

This section separates the rules that are **executable gates** (every one maps to a `BP-NNN` entry in `RULE_TO_GATE_CATALOG.md` backed by a test) from **contributor conventions** (guidance that the baseline expects reviewers to apply but that no automated gate enforces). The split exists so that a reader of this file can tell at a glance which rules the build will break on and which rules are the review's responsibility. If you find a rule in the Gated section that has no matching BP-NNN, flag it — do not file a new conventions entry as a workaround.

### Gated Non-Negotiables

Every rule here is mapped to a BP-NNN catalog entry plus an executable gate. A PR that violates one of these fails the build.

- **ApiHost is composition-only.** (BP-001) No handlers, persistence, or feature-specific endpoint logic there. It must not import any `{Module}.Api` namespace — modules that need rate-limiter policies implement `IModuleRateLimiterContributor` so they own their own policy registration (`ApiHostMayNotImportModuleApiNamespaces` enforces this).
- **Every use case goes through the dispatcher.** (BP-002) No direct handler invocation from endpoints.
- **PublicContracts is not a transport-DTO dumping ground.** (BP-004) Browser transport DTOs live in `.Api`, not `.PublicContracts`. Every query namespace in `.PublicContracts` must include at least one governing service interface, and every non-interface type must be reachable from a service interface or compose reachable types.
- **Do not widen BuildingBlocks casually.** (BP-005) A new or expanded approved shared public surface in `src/BuildingBlocks/*` is allowed only when it is technical or universal, collapses duplicate paths into one canonical mechanism, removes more complexity than it introduces, and lands with the matching shared-abstraction guardrail update in the same review. Each shared project is also size-bounded by `governance/buildingblocks-budget.json`; casual accretion fails `BuildingBlocksGrowthBudgetTests` until the budget is raised in the same commit with a justification.
- **Authorization is role-based.** (BP-014) To gate a command or query, add a `RoleRequirement` (referencing `IdentityRoles.Admin`, `IdentityRoles.User`, or `IdentityRoles.Machine`) to its `AuthorizationRequirements` collection. There is no `PermissionCatalog`, `{Module}Permissions`, or `PermissionDefinitionProvider`; the dispatcher's authorization pipeline enforces role requirements, and endpoint-level `[Authorize]` is a supplement. Session invalidation rides the ASP.NET Core Identity security stamp via `UserManager.UpdateSecurityStampAsync`. Browser auth stays on cookie sessions — no JWTs, no browser token storage.
- **Antiforgery is middleware-level.** (BP-014) Browser mutation antiforgery is enforced centrally by `BrowserMutationAntiforgeryMiddleware`. Machine-authenticated requests are exempt. Do not add endpoint-level `antiforgery.ValidateRequestAsync` calls — the middleware already covers all browser mutations.
- **CI gate parity is enforced.** (BP-022) The canonical CI gate manifest at `governance/ci-gates.json` is the single source of truth for what each gate runs and the order they run in. Both `.github/workflows/ci.yml` and `scripts/Invoke-LocalGates.ps1` dispatch every gate through `scripts/Invoke-CiGate.ps1 -Id <id>`. Add or reorder a gate in the manifest, never inline. Run `pwsh scripts/Invoke-LocalGates.ps1` as the local pre-push check; it executes every gate in CI's exact order and fails fast. The `LocalGateParityTests` architecture test fails if the workflow drifts from the manifest.
- **No commercial packages.** (BP-025) Commercial/paid dependencies are not used. Prefer OSS or build it.
- **Shared runtime durability has no silent fallback.** (BP-027) The supported composed runtime requires `ConnectionStrings:BaselineDatabase`; do not restore in-memory durability fallback under the guise of local convenience.
- **No CORS unless an ADR says so.** (BP-028) Same-origin is the default. When CORS is explicitly enabled, origins/methods/headers/exposed-headers are all wildcard-free allow-lists bound via `FrontendCorsOptions` and `ValidateOnStart`-guarded.
- **Authentication endpoints are rate-limited.** (BP-030) Login and step-up are limited per IP; password changes per authenticated user. Do not bypass or weaken these policies.
- **Public list endpoints use cursor pagination.** (BP-032) Use `CursorPagedResult<T>` and `CursorEncoding` for public-facing list endpoints. Do not use offset pagination. Bounded non-cursor management lists must carry `.WithNonCursorListEndpoint("<reason>")` from `BuildingBlocks.Infrastructure.Http`, which `PublicListEndpointPaginationGuardrailTests` enforces.
- **Cross-module reference headroom is visible.** (BP-033) Before adding a new cross-module `*.PublicContracts` reference, run `pwsh scripts/Report-ModuleDependencies.ps1` to see current headroom against the cap of 3.
- **Frontend forms use schema-driven validation.** (BP-037) Every `<form>` component under `web/src/**` is driven by React Hook Form with a `zodResolver` schema, and every input bound to a validated field renders `aria-invalid` and `aria-describedby`. A disabled submit button is never the sole validation UI. Enforced by `FrontendFormValidationGuardrailTests`.

### Contributor Conventions

The rules here are review-time guidance that no automated gate enforces. Treat them as binding, but don't expect the build to tell you when you're violating them.

- **Do not retrofit the architecture.** If a task seems to require changing `BLUEPRINT.md`'s non-negotiables to make code work, stop and surface the conflict to the user. The blueprint is intentionally upfront.
- **Optimize toward the industrialized end-state.** Treat this repo as a production-grade target architecture, not as a baseline that should accumulate convenience shortcuts. When choosing between a temporary shortcut and the durable end-state shape, prefer the industrialized design. Development-only aids documented in local workflow materials do not redefine the target architecture, and any non-production compromise that still matters architecturally must be explicit in an accepted ADR.
- **Structural rules are executable.** Before adding a module, wiring, or shared type, confirm the corresponding gate exists in `RULE_TO_GATE_CATALOG.md`. If a rule has no enforcement path, flag it — do not file a convention entry as a workaround.
- **Do not create hidden platform seams.** Dispatcher, module runtime, outbox or inbox delivery, and the scaffold plus governance engine are baseline-owned platform subsystems. Extend their canonical entry points or surface the gap; do not add alternate composition, worker, or structural-onboarding paths beside them.
- **Tests come first.** TDD is mandatory per `BLUEPRINT.md`. A change without the required test category is incomplete.
- **No hardcoded secrets.** Docker Compose and configuration files use environment variable references. Development credentials come from `.env` (gitignored) or user-secrets, not from committed literals. This is reviewer-enforced today; treat any drift as a fix, not an exemption request.
- **Temporary implementation runbooks stay local.** Put engineering plans, refactor runbooks, and other temporary working docs under `docs/_local/` (gitignored), not `docs/runbooks/`. Reserve `docs/runbooks/` for durable operator or OSS-facing runbooks. Delete the local runbook once the implementation is complete so the workspace does not accumulate stale execution plans.

## Operational Runbooks

When responding to a production incident or operator alert, consult the runbook that matches the alerting surface before taking corrective action:

- [`docs/runbooks/outbox-dead-letter-recovery.md`](docs/runbooks/outbox-dead-letter-recovery.md) — operator recovery for non-empty outbox dead-letter queries (the deliberate "visibility without replay" model from `docs/adr/ADR-OUTBOX-DEAD-LETTER-VISIBILITY.md`).
- [`docs/runbooks/outbox-dead-letter-classification.md`](docs/runbooks/outbox-dead-letter-classification.md) — classification of deterministic dispatch failures (unresolvable type, deserialization, id mismatch) and the supported handling.
- [`docs/runbooks/outbox-signal-degraded.md`](docs/runbooks/outbox-signal-degraded.md) — recovery when the outbox signal listener reports a degraded state.

Related reference material for platform operators and deployers:

- [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) — required configuration surface for deploying the ApiHost to a real environment (database, Identity bootstrap, OpenTelemetry, ASP.NET Core).
- [`docs/MIGRATION_ROLLBACK.md`](docs/MIGRATION_ROLLBACK.md) — the BP-020 expand-and-contract policy, supported reversal mechanisms, and the decision tree for `--rollback-to` vs application rollback vs restore-from-backup.
- [`docs/SECRET_MANAGEMENT.md`](docs/SECRET_MANAGEMENT.md) — the secret-source model and the naming conventions module authors follow for connection strings, module options, and external credentials.

## The Documentation Freshness Rule

The governed documentation set is treated as one unit. A change to any one file in the set requires updates to every other file it affects in the **same review**.

**Governed set:**

- `docs/BLUEPRINT.md`
- `docs/QUALITY_GATES.md`
- `docs/ADD_MODULE.md`
- `docs/RULE_TO_GATE_CATALOG.md`
- `docs/adr/*`
- `README.md`
- `AGENTS.md` (this file)

**What triggers updates:**

- A change to a non-negotiable in `BLUEPRINT.md` → update `QUALITY_GATES.md`, `RULE_TO_GATE_CATALOG.md`, and add/update an ADR if the default shape changed.
- A change to a gate or enforcement path → update `RULE_TO_GATE_CATALOG.md` and (if the rule wording moved) `BLUEPRINT.md`.
- A change to the module-add workflow → update `ADD_MODULE.md`, `QUALITY_GATES.md` (scaffold gate), and the catalog.
- A new non-negotiable → new rule ID in `RULE_TO_GATE_CATALOG.md` plus a mapped enforcement artifact plus updated `BLUEPRINT.md`.
- A change to local workflow (bootstrap, scripts, frontend) → update `README.md`.
- A change to agent-facing conventions → update `AGENTS.md`.

This rule is tracked as **BP-026** in `RULE_TO_GATE_CATALOG.md`.

## Fix Quality — No Shortcuts Without A Waiver

Apply the best-practice fix for the problem, not the cheapest change that makes the symptom go away. Do not introduce:

- workarounds, shims, or compatibility shunts that bypass a rule instead of satisfying it
- dual code paths added "temporarily" to avoid touching the real one
- `try`/`catch` that swallows a failure rather than addressing its cause
- feature flags or config toggles whose only purpose is to hide an unfinished fix
- copy-pasted code that avoids editing a shared abstraction
- `// TODO: fix properly later` on code that is shipping now
- renamed or silenced tests that fail for the real reason they exist

If the best-practice fix is genuinely out of scope for the current change, that is an **explicit dated waiver**, not a hidden shim. File it per `docs/QUALITY_GATES.md` (owner, created date, expiry, linked follow-up, removal condition) so the debt is visible and ratcheted rather than discovered later.

The distinction is not shortcut-vs-no-shortcut; it is **declared-vs-hidden**:

- A **waiver** is named, dated, owned, scoped to one rule, and expires. Acceptable.
- A **shim** is undeclared, undated, unowned, and has no removal condition. Not acceptable.

Do not treat a shim or quick fix as an available option. The only paths you may take are:

- apply the best-practice fix, expanding scope if needed to cover the real problem, or
- if that fix is genuinely out of scope, surface the choice to the user and propose a waiver entry with an expiry and removal condition.

Never merge a shim silently, and never describe a shim as "a quick fix for now" without the matching waiver.

## Skills And Tooling

Agents should not silently invoke heavy skills based on keyword matches. If a skill would help (for example, `/modulith-audit`), surface the suggestion to the user and wait for explicit approval before running it.

## When In Doubt

If an instruction conflicts with a non-negotiable, the non-negotiable wins. Surface the conflict to the user with a reference to the specific rule ID rather than silently working around it.
