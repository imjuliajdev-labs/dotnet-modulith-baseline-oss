# ADR: Direct Durable Reads As The Baseline For Module-State Propagation

- Status: Accepted
- Date: 2026-04-10
- Owners: Baseline maintainers

## Context

The governed runtime-module-state posture requires reversible, cluster-safe enable/disable behavior that fails closed under uncertainty and remains safe during multi-instance drains.

The implementation already persists module state durably in PostgreSQL and uses direct durable reads plus advisory-lock coordination at request, worker, and transition boundaries. The previous blueprint wording described a richer default involving a versioned provider with push invalidation and polling fallback.

That wording was ahead of the implementation and stricter than the baseline the repo actually relies on today.

## Decision

For this baseline, the module-runtime subsystem uses direct durable reads as its required baseline.

Canonical entry points:

- `IModuleStateReader`
- `IModuleStateGuard`
- `IModuleExecutionGate`
- `IModuleWorkLeaseManager`
- `ModulePollingBackgroundService`

Subsystem invariants:

1. Module state is persisted durably in PostgreSQL.
2. Guards read the current durable state directly at module-execution gate boundaries.
3. Transition safety relies on durable state, optimistic concurrency, and PostgreSQL advisory locks.
4. Request, integration-event, outbox-dispatch, worker, polling, recovery, and scheduled execution all enter through the shared `IModuleExecutionGate` rather than feature-local enable or disable checks.
5. The gate is responsible for both state confirmation and drain-participating work-lease acquisition.
6. Optional push invalidation remains a future optimization, not a required baseline capability.

This is a repo-default decision about the supported baseline, not a claim that direct durable reads are the only valid way to build runtime module activation.

## Consequences

- The blueprint now matches the implemented runtime-state baseline more closely.
- Multi-instance safety continues to rely on the existing integration-tested durable-read and advisory-lock model.
- Regression expectations are explicit: architecture tests guard the canonical runtime entry points, unit tests cover gate semantics, and integration tests cover persistence, activation, drain, and fail-closed behavior.
- Future work may still add a cached provider, push invalidation, or polling-based refresh optimizations without requiring another architecture reset, as long as the fail-closed durable baseline remains intact.
