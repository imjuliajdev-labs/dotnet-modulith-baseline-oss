# ADR: Outbox Dead-Letter Visibility Without Replay

## Status

Accepted

## Context

Dead-lettered integration events need to be visible to operators for monitoring and diagnosis. The question is whether to provide replay (re-dispatch) capability alongside visibility.

## Decision

Provide read-only dead-letter visibility through an admin query endpoint. Do not implement replay.

Replay requires:
- Ensuring ordering guarantees are not violated
- Handling consumers that may have already compensated
- Addressing idempotency across the entire handler chain
- Defining what happens when replay itself fails

These are non-trivial design decisions that require per-consumer analysis.

## Consequences

- Operators can monitor and diagnose dead-letter accumulation
- Metrics enable alerting on dead-letter rate
- Replay is a future capability that requires its own ADR with per-consumer safety analysis
