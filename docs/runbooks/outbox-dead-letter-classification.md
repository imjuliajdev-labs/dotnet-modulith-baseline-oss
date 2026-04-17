# Outbox Dead-Letter Classification Reference

Operator lookup for the error codes carried on dead-lettered integration-event rows. Each code is stored in the outbox `last_error_code` column and surfaced through `GET /api/v1/platform/outbox/dead-letters`. A row's error code identifies whether the failure is operator-actionable or developer-actionable.

## Error codes

| Error code                | Category                    | Dead-letter timing      | Remediation                                                                                                                                                                                                                                                             |
| ------------------------- | --------------------------- | ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `outbox.type_unresolvable`| Deterministic (developer)   | Immediate, 1st attempt | The stored `event_type` does not map to any loaded `IIntegrationEvent` in the running composition. Either a publisher wrote a type from a module that is no longer deployed, or the consumer host is missing the assembly that owns the type. Redeploy the consumer host with the owning module composed in, or publish a data-fix that deletes/remaps the orphaned row. Retrying the row is guaranteed to fail again until the code change lands. |
| `outbox.deserialize_failed`| Deterministic (developer)  | Immediate, 1st attempt | The stored JSON payload does not deserialize to the stored `event_type`. Likely causes: a contract-breaking change in the event's DTO shape (e.g., renamed required property), a manual SQL patch that corrupted the payload, or a publisher version mismatch. Open the dead-letter row through the admin query, inspect the raw payload and the DTO definition, and produce a migration that either rewrites the payload to the current shape or tombstones the row. |
| `outbox.event_id_mismatch`| Deterministic (developer)   | Immediate, 1st attempt | The `event_id` column and the `eventId` embedded in the deserialized payload disagree. This is a publisher-side bug or a corrupted row. Inspect the publisher's enqueue path (are you constructing a new `Guid` between persisting the column and serializing the payload?) and tombstone the row. This code never self-heals; retrying is wasted I/O.                                                                                             |
| `unexpected.failure`      | Transient / handler fault    | After `DeadLetterThreshold` attempts | The consumer handler threw, and no other mapping matched. Retry budget was honored. Inspect the `last_error_message` and the handler logs; most rows here are transient infrastructure issues (Postgres reachability, downstream HTTP timeout) and the row may be safe to leave for manual re-queue once the underlying dependency recovers.                                                                        |
| `persistence.*`           | Transient / infrastructure   | After `DeadLetterThreshold` attempts | Specific Postgres error states captured by `DefaultExceptionToErrorMapper` (unique violation, foreign key violation, etc.). Diagnose the upstream data-model issue before replaying.                                                                                                                                                                                                                                                     |
| `service.timeout`         | Transient / dependency       | After `DeadLetterThreshold` attempts | A downstream dependency hit its per-call timeout. Investigate the dependency's health; if transient, retry is safe; if chronic, raise an incident for the dependency owner.                                                                                                                                                                                                                                                                |

## Why immediate dead-letter for deterministic failures

`IntegrationEventOutboxDispatcher` normally respects `IntegrationEventOutboxProcessingOptions.DeadLetterThreshold` so transient failures have room to self-heal. The three deterministic codes above (`outbox.type_unresolvable`, `outbox.deserialize_failed`, `outbox.event_id_mismatch`) bypass that threshold and dead-letter on the **first** attempt. These failures never recover without human intervention, and burning `DeadLetterThreshold` attempts on them only:

- delays the row's visibility on the dead-letter admin query, and
- generates noise in the outbox retry telemetry that makes real transient failures harder to spot.

The dispatcher recognizes these by catching `IntegrationEventOutboxDispatchException` (the shared base of the three typed exceptions). A handler fault that happens to share a code with one of the above would not derive from this base type and would use the normal retry budget path.

## Related

- `src/BuildingBlocks/Infrastructure/IntegrationEvents/IntegrationEventOutboxExceptions.cs` — typed exceptions with `MessageId`, `ModuleKey`, `EventType` carried as properties (not embedded in the message string).
- `src/BuildingBlocks/Infrastructure/IntegrationEvents/IntegrationEventOutboxDispatcher.cs` — the dispatch loop that throws the typed exceptions and applies the immediate dead-letter branch.
- `src/BuildingBlocks/Infrastructure/ProblemDetails/DefaultExceptionToErrorMapper.cs` — the mapping from exception type to error code.
- `docs/runbooks/outbox-dead-letter-recovery.md` — the procedure for replaying or tombstoning a dead-lettered row once you have classified it.
- `tests/Integration.Tests/ChaosScenarios/OutboxResilienceChaosTests.cs` — executable coverage of each deterministic failure path.
