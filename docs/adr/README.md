# ADRs

This folder holds Architecture Decision Records for permanent or long-lived architectural choices.

Use an ADR when a change affects the intended end-state architecture, the default platform shape, or the supported extension model.

## How To Read These ADRs

These ADRs record the intentional defaults for this baseline.

They are not universal claims that every modular monolith should make the same choice. A good ADR in this repo explains why this baseline chose a path, which tradeoffs it accepts, and what adopters would be taking on if they chose to replace that default.

Prefer wording like "for this baseline, we choose X because..." over wording that implies alternatives are categorically wrong.

Typical ADR topics in this baseline include:

- changing the external HTTP versioning shape
- changing the real-time transport strategy
- changing the machine-client authentication model
- changing runtime module-state propagation mechanics
- changing the scaffold contract or shared-layer governance model

Do not use an ADR for a temporary exception.

Temporary exceptions belong in versioned allowlist or waiver files with an expiry and a removal condition.

## File Naming

Use the repository's descriptive `ADR-...` filename convention consistently.

Example:

- `ADR-CUSTOM-DISPATCHER.md`
- `ADR-MODULE-MIGRATION-POSTURE.md`

## Minimal Template

```md
# ADR: Title

- Status: Proposed | Accepted | Superseded
- Date: YYYY-MM-DD
- Owners: Team or named owners
- Supersedes: optional ADR id

## Context

Why this decision is needed.

## Decision

The selected architectural choice.

## Consequences

Expected benefits, costs, and follow-up constraints.
```

## ADR vs Waiver

- ADR: defines the intended architecture.
- Waiver: records a temporary, narrowly scoped failure against the intended architecture.
- If a temporary exception becomes the new intended default, remove the waiver and replace it with an ADR-backed rule change.
