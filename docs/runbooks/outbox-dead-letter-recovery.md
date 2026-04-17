# Outbox Dead-Letter Recovery Runbook

Operator recovery guide for entries surfaced by the platform outbox dead-letter visibility query. This runbook is the missing operational half of [`ADR-OUTBOX-DEAD-LETTER-VISIBILITY`](../adr/ADR-OUTBOX-DEAD-LETTER-VISIBILITY.md), which deliberately omits a replay endpoint.

## When this runbook fires

- An on-call alert triggers on a non-empty `GET /api/v1/platform/outbox/dead-letters` response, **or**
- An operator notices dead-letter accumulation while inspecting platform telemetry.

A non-empty dead-letter set means at least one integration event exhausted its retry budget without being acknowledged by every consumer. The originating business outcome may or may not have been delivered downstream.

## Diagnosis

Before deciding on recovery, gather facts:

1. **Inspect the dead-letter record.** Use the platform admin query to retrieve the full payload, headers, source module, target consumer, original publish timestamp, retry count, and last failure reason.
2. **Identify the source module.** The `source` field on the dead-letter record names the module whose outbox emitted the event. Owner-of-record for diagnosis is that module's team.
3. **Identify the business outcome.** Map the event type back to the originating command or domain action. Ask: "what user-visible state should exist if this event had been delivered successfully?"
4. **Check for duplicate evidence.** Read the consumer-side inbox / projection to see whether the consumer already processed an earlier copy of this event under the same idempotency key. If yes, the dead-letter is a benign artifact of a transient failure that the consumer eventually absorbed — drop it after confirming.

## Recovery decision tree

### Branch 1 — Transient downstream failure

Symptoms: the failure reason is a network timeout, a temporary 5xx, a connection reset, or any error class the consumer is designed to retry.

Action:
- Wait for the next retry window (the outbox dispatcher will not retry a dead-lettered entry on its own — that is intentional).
- Re-check the consumer inbox after the downstream service recovers.
- If the consumer's projection now reflects the expected state, the recovery is complete; the dead-letter record can be dropped via the admin query once the root cause is documented.
- If not, escalate to Branch 3.

### Branch 2 — Poison message (schema mismatch)

Symptoms: the failure reason is a deserialization error, a contract version mismatch, or a validation failure that will not improve with retries.

Action:
- File the issue against the source module owner identified in diagnosis step 2. The event payload itself is broken; replaying it will only reproduce the error.
- Do **not** attempt re-issuance.
- Once the source module owner has shipped a fix (typically a corrected publisher or a contract version migration), drop the dead-letter record via the admin query.
- If the broken payload represents a real business outcome that was never delivered, the source module owner is also responsible for re-issuing the originating command through their normal API once the publisher fix is in place.

### Branch 3 — Business-critical missed outcome

Symptoms: a downstream consumer never observed the event, the underlying business outcome is real and matters (a payment, a notification, a state transition), and Branch 1's wait did not resolve it.

Action:
- Re-issue the originating command through the source module's public API. Idempotency keys make this safe — the dispatcher's idempotency pipeline will deduplicate if any partial delivery already occurred (BP-013).
- Document the re-issuance in an incident note: dead-letter id, originating command, idempotency key used, who authorized the re-issue, timestamp.
- After confirming the consumer projection now matches expected state, drop the dead-letter record via the admin query.

## Antipatterns — do not do this

- **Do not implement a replay endpoint** as a "quick fix". The ADR rejected replay because it requires per-consumer ordering, idempotency, and compensation analysis. A replay endpoint added under operator pressure is exactly the design the ADR exists to prevent.
- **Do not edit outbox or dead-letter rows directly in the database.** Use the admin query and supported domain commands. Direct edits bypass the auditability the platform module guarantees.
- **Do not disable the dead-letter visibility gate** to silence alerts. Visibility is the only reason this runbook can exist; suppressing it converts an operational signal into a hidden failure.
- **Do not "drain" the dead-letter store** by mass-dropping records without diagnosis. Each entry represents a real prior failure and may indicate an ongoing producer or consumer bug.

## References

- [`ADR-OUTBOX-DEAD-LETTER-VISIBILITY.md`](../adr/ADR-OUTBOX-DEAD-LETTER-VISIBILITY.md) — the decision this runbook operationalizes
- [`ADR-CUSTOM-OUTBOX-INBOX.md`](../adr/ADR-CUSTOM-OUTBOX-INBOX.md) — the surrounding outbox/inbox design
- BP-013 (durable command idempotency) and BP-019 (at-least-once delivery, replay-safe consumers) in [`docs/RULE_TO_GATE_CATALOG.md`](../RULE_TO_GATE_CATALOG.md)
