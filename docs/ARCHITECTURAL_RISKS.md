<!-- doc-tier: maintained-narrative -->
# Architectural Risks

This document captures the main architecture risks implied by the current baseline.

It is a critique and preservation aid, not a governing document. It does not replace or override:

- [`BLUEPRINT.md`](BLUEPRINT.md)
- [`QUALITY_GATES.md`](QUALITY_GATES.md)
- [`ADD_MODULE.md`](ADD_MODULE.md)
- [`RULE_TO_GATE_CATALOG.md`](RULE_TO_GATE_CATALOG.md)

Use this document to understand where the baseline is strongest, where it is most expensive to maintain, and which architectural concerns deserve the most active attention.

## How to read this document

The risks below are not claims that the architecture is unsound. In most cases, they are the costs of deliberate strengths in the baseline.

In practice, the most important risks in this repository are not shortcut risks. They are:

- concentration risks around baseline-owned platform seams
- consistency risks across advanced runtime behavior
- adoption risks where the baseline can be misunderstood or over-trusted
- maintenance risks caused by the cost of keeping the governed system aligned

## Priority summary

| Priority | Risk | Why it matters |
| --- | --- | --- |
| P1 | Baseline-owned core seam concentration | Too much repo-wide behavior depends on custom platform code |
| P1 | Module-state execution convergence drift | One of the baseline's signature promises depends on the shared execution gate staying aligned across execution paths |
| ~~P1~~ resolved 2026-04-11 | ~~Identity adoption hazard~~ | Resolved by the role-based production-grade Identity refactor; see entry 3 below |
| P2 | Reference-module interdependence | Teaching realism increases adoption and cleanup complexity |
| P2 | Persistence posture misread risk | Adopters can mistake specialized PostgreSQL patterns for the default feature-module path |
| P2 | Governance freshness burden | The architecture only remains credible while docs, gates, scaffold, and code stay aligned |
| P3 | Full validation environment cost | The strongest quality wall depends on non-trivial local and CI setup |
| P3 | Frontend generation-discipline dependence | The frontend posture is only strong while generated-contract discipline remains intact |
| P3 | PublicContracts expansion pressure | Successful modular systems always face pressure to widen shared contracts beyond their intended role; now mitigated by a per-module cross-module reference cap (BP-033) |

## P1 risks

### 1. Baseline-owned core seam concentration

The baseline owns a large amount of critical shared behavior directly, including:

- the dispatcher and pipeline ordering
- the outbox and inbox posture
- runtime module activation and state coordination
- scaffold and governance validation tooling

Each of those choices is defensible on its own. Together, they create a concentration risk: subtle drift or bugs in one of these seams can affect the entire repository.

#### Why this matters

These are not leaf concerns. They are central execution and composition concerns. If they degrade, the baseline degrades system-wide.

#### What to watch

- dispatcher behavior growth without corresponding simplification elsewhere
- new baseline-owned abstractions being added faster than shared complexity is retired
- weak ownership around shared execution seams

#### Recommended response

Treat these seams as explicit platform surfaces with named ownership, regression expectations, and active maintenance rather than as incidental implementation details. The chaos / failure-mode integration tests under `tests/Integration.Tests/ChaosScenarios/` exercise the real outbox dispatcher, real Postgres driver, and real module-execution gate against injected faults so that "the platform survives a transient database fault and a poison message dead-letters with an observability signal" stops being a claim and becomes a test that fails when the mechanism is broken.

### 2. Module-state execution convergence drift

Runtime module activation is one of the repository's most distinctive capabilities. The earlier request-vs-worker split has been converged behind the shared module-execution gate, and that closure is recorded in [`BASELINE_DECLARATION.md`](BASELINE_DECLARATION.md).

The remaining risk is preservation drift: future work could reintroduce divergence across HTTP requests, dispatcher execution, integration-event handling, outbox dispatch, and worker execution even though the intended architecture is now one converged control model.

#### Why this matters

The module-state system is only as strong as its most weakly aligned execution path. If request execution, integration-event execution, and worker execution evolve differently, the baseline can begin to promise one runtime model while implementing several similar but not identical ones.

#### What to watch

- bug fixes landing in one execution path but not another
- disable and re-enable semantics diverging across HTTP, handlers, and workers
- increasing reliance on special-case worker coordination logic

#### Recommended response

Keep new execution paths on the shared module-execution gate, and treat any future divergence as an explicit architecture change rather than allowing it to creep back in as implementation drift. `OutboxResilienceChaosTests.DisablingModuleAfterEnqueueLeavesOutboxRowsParkedAndReEnableDeliversExactlyOnce` proves the disable / re-enable cycle leaves no orphaned outbox rows; pair it with `OutboxDispatchSkipsDisabledModulesUntilTheyAreReEnabled` in `IntegrationEventOutboxIntegrationTests` for the established baseline.

### 3. Identity adoption hazard — RESOLVED 2026-04-11

**Status: Resolved.** The Identity posture ambiguity was closed by the Phases 1-6 Identity refactor. The `Identity` module is now explicitly a production-grade implementation built on standard ASP.NET Core Identity, cookie authentication, EF Core, and role-based authorization, behind the existing module boundary. There is no longer a "baseline vs. production" ambiguity for adopters to misread.

Key elements of the resolution:

- ASP.NET Core Identity with cookie authentication is the production answer, not a stub
- role-based authorization (`Admin`, `User`, `Machine`) replaced the custom permission catalog
- session invalidation rides ASP.NET Core Identity's standard security stamp
- the obsolete `IDENTITY_HARDENING_PLAN.md` and `IdentityReplaceabilityGuardrailTests` are deleted
- seeded admin and seeded machine credentials are Development/Testing only with no override flag; any other environment fails fast at startup
- the `AllowUnsafeSeededCredentialsInNonDevelopment` escape hatch is removed

See [`adr/ADR-IDENTITY-POSTURE.md`](adr/ADR-IDENTITY-POSTURE.md) for the governing decision.

Teams that still require a different identity provider can replace `Identity.Infrastructure` with their own implementation, but the baseline no longer asks adopters to do so.

## P2 risks

### 4. Reference-module interdependence

The teaching modules are intentionally realistic, which means they are not perfectly isolated from one another. Cross-module examples help demonstrate the supported seams, but they also make the teaching set interdependent.

#### Why this matters

This is a good tradeoff for proving the architecture, but it makes adoption and cleanup more complex. It is especially relevant when adopters try to remove only some of the reference modules.

#### What to watch

- forks that remove one teaching module and leave broken shared-query or event-consumer examples behind
- confusion about whether the teaching modules are intended to be understood as a set or independently
- rising adoption friction caused by partial cleanup complexity

#### Recommended response

Continue documenting the teaching modules as a coordinated reference set and keep adoption guidance explicit about removal order, cleanup scope, and reference-module dependencies.

### 5. Persistence posture misread risk

The repository has a coherent persistence posture:

- EF Core is the default relational path
- specialized direct PostgreSQL access is allowed when justified
- both styles remain behind the same migration and durability expectations

The risk is that new adopters may read the live code and conclude that EF-backed and specialized store-backed patterns are equally default.

#### Why this matters

If that misunderstanding spreads, teams may adopt specialized store patterns too early and bypass the simpler governed path for normal relational features.

#### What to watch

- teams reaching for specialized PostgreSQL stores before trying the EF-first path
- module examples being cited without the teaching context around them
- erosion of the baseline's intended EF-first teaching path

#### Recommended response

Keep `Blog` and the EF persistence guidance prominent as the default reference path, and continue describing `KnowledgeBase` and similar store-backed modules as specialized references rather than the default module model.

### 6. Governance freshness burden

One of the baseline's core strengths is that documentation, scaffold, tests, rule catalog, ADRs, and CI shape are treated as one governed system.

That is also a maintenance risk. The architecture remains credible only while contributors keep all those assets aligned.

That burden has grown as the baseline added more operational and governance surface: format enforcement, dependency-vulnerability scanning, CodeQL, Dependabot, deployment guidance, migration-rollback guidance, and telemetry-collector wiring all improve confidence, but they widen the set of assets that can drift out of sync.

#### Why this matters

If the cost of staying aligned becomes too high, contributors will eventually begin to bypass the governed system instead of improving it.

#### What to watch

- growing friction around updating the governed document set
- implementation changes landing without the corresponding catalog, ADR, or scaffold updates
- contributors treating governance updates as optional cleanup rather than part of the architectural change

#### Recommended response

Keep the governed documents concise, continue pruning redundant wording, and treat governance usability as part of architecture quality rather than as secondary documentation work.

## P3 risks

### 7. Full validation environment cost

The strongest quality wall depends on a non-trivial environment, including PostgreSQL, Docker or Testcontainers, and the frontend test toolchain.

That cost is no longer just about getting the app and tests to run. The supported posture now also includes stronger CI and security checks plus a more realistic deployment and telemetry story, which improves trustworthiness but raises the bar for anyone trying to reproduce the full baseline experience locally.

#### Why this matters

This is the right tradeoff for parity and realism, but it raises the cost of full local validation. Over time, that can lead teams to rely more on partial validation than the baseline intends.

#### What to watch

- frequent local validation that skips the strongest integration paths
- contributors treating CI as the only place where the real architecture is exercised
- new tests increasing setup cost faster than they increase architectural confidence
- operational or telemetry changes being merged with only partial local reproduction because the full posture is too expensive to exercise routinely

#### Recommended response

Keep CI authoritative, keep local smoke paths fast, and be selective about adding heavyweight validation that does not materially improve architectural confidence.

### 8. Frontend generation-discipline dependence

The frontend architecture is strong, but its strength depends on discipline around:

- generated contracts
- RTK Query wrappers
- manifest-based lazy loading
- shared realtime and telemetry adapters

#### Why this matters

The remaining auth-related frontend risk is convergence drift, not posture ambiguity. If generated artifacts drift or feature code begins bypassing the shared path, the frontend can gradually move away from the governed role-based and public-route posture even while the repository still describes the stronger shape.

#### What to watch

- stale generated contract artifacts
- hand-maintained transport mirrors creeping into feature code
- feature-local HTTP or realtime exceptions accumulating

#### Recommended response

Keep contract generation friction low, keep frontend rules explicit, and continue protecting the shared path with tests and architecture guardrails.

### 9. PublicContracts expansion pressure

The current baseline governs `.PublicContracts` well, but successful modular systems always face pressure to widen their shared surface over time.

#### Why this matters

If `.PublicContracts` becomes a convenience bucket for shared DTOs, read models, or cross-module shortcuts, one of the baseline's most important boundaries will erode gradually rather than obviously.

#### What to watch

- new shared contracts without a clearly identified consumer need
- browser-facing transport shapes drifting into `.PublicContracts`
- convenience-sharing arguments replacing explicit public-contract intent

#### Mitigation added 2026-04-12

`CrossModuleReferenceCapGuardrailTests` now enforces a maximum of 3 distinct cross-module PublicContracts references per module. `SharedReadFallbackGuardrailTests` ensures all shared read adapters declare explicit timeout, freshness, and fallback policies. `EfQueryProjectionGuardrailTests` ensures query and reader paths prefer `.Select()` projections over `.Include()` eager loading.

#### Recommended response

Keep `.PublicContracts` narrow, require explicit justification for new shared contracts, and prefer projections or explicit query services over cheap type sharing.

## What is already strong

The following architectural choices are currently strong and should be preserved unless there is a deliberate governed change:

- `ApiHost` as composition-only
- one public `IApiModule` per module
- scaffold-first structural change workflow
- cookie auth plus antiforgery with same-origin default posture
- generated browser contracts
- PostgreSQL-only supported runtime posture
- executable architecture and governance tests
- explicit runtime module activation semantics
- treatment of docs, tests, scaffold, and CI as one governed system

## Recommended focus order

If the repository's maintainers want to reduce risk without weakening the architecture, the most valuable order of attention is:

1. keep hardening module-state execution consistency
2. treat dispatcher, module-state, outbox or inbox, and scaffold as explicitly owned platform surfaces
3. keep governance concise enough that contributors continue using it instead of working around it
4. protect the EF-first teaching path and the narrow meaning of `.PublicContracts`

## Final view

The most important point is this:

> The baseline's main architectural risks are mostly the costs of deliberate strength, not signs of architectural incoherence.

This repository is strongest where many similar codebases are weakest:

- boundary definition
- governance
- drift resistance
- runtime operational posture
- extension-path clarity

That strength should be preserved by managing its maintenance costs explicitly rather than by relaxing the architecture silently.
