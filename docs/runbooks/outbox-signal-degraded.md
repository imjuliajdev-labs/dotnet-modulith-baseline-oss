# Outbox Signal Degraded Runbook

Operator response guide for a degraded outbox LISTEN/NOTIFY listener. This runbook is the operational surface of the `outbox_signal` readiness health check and the `outbox.signal.degraded` gauge exported under the `BuildingBlocks.Outbox` meter.

## When this runbook fires

- The readiness probe reports `outbox_signal` as `Degraded`, **or**
- The `outbox.signal.degraded` gauge moves from `0` to `1`, **or**
- An operator sees a `Failed to start outbox signal listener; falling back to poll-only mode.` warning in the hosted-service log.

The system is **not** unhealthy in this state. `IntegrationEventOutboxHostedService` continues to drain each module's outbox on the poll interval (default 50 ms in tests, module configuration in production). What is lost is the low-latency push path: enqueue-to-dispatch latency falls back to the poll cycle instead of being triggered by the `outbox_dispatch` channel.

## Diagnosis

1. **Read the last-failure message.** The `outbox_signal` health check description includes the string stored in `IntegrationEventOutboxSignalStatus.LastFailure`. This is the exception message captured when `StartListeningAsync` threw.
2. **Inspect the hosted-service logs.** Look for `Failed to start outbox signal listener` (startup failure) or `Outbox LISTEN/NOTIFY listener stopped unexpectedly` (listener died mid-flight). The accompanying exception carries the Postgres error code and connection identity.
3. **Check Postgres reachability from the ApiHost.** The listener uses the same connection string as the rest of the shared runtime (`ConnectionStrings:BaselineDatabase`). A degraded signal is almost always a symptom of: (a) Postgres restart, (b) network partition between ApiHost and Postgres, (c) `max_connections` exhaustion, or (d) a role / pg_hba rule that denies `LISTEN`.
4. **Confirm dispatch is still progressing.** Query the outbox table for the affected modules and verify that `dispatched_at` timestamps are moving forward on the poll interval. If polling has also stalled, this is no longer a signal-only problem — escalate to a full outbox incident and treat it as P1.

## Remediation

- If Postgres was restarted or the listener connection was dropped, the hosted service does **not** currently auto-reconnect the LISTEN. Rolling the ApiHost instance (or the single process in local dev) is the supported recovery — after restart the hosted service reattempts `StartListeningAsync` and `IntegrationEventOutboxSignalStatus` flips back to `Listening` on success.
- If the underlying error is a Postgres authorization failure on `LISTEN`, grant the role the ability to execute `LISTEN outbox_dispatch` on the target database. The baseline does not mask this failure: the health check will continue to report degraded until the grant is in place.
- If the underlying error is transient (brief network blip), you may choose to wait out the next natural deployment rather than forcing an immediate roll. In that window, dispatch latency is capped by `IntegrationEventOutboxProcessingOptions.PollInterval` — make sure your SLOs tolerate it.

## Exit criteria

- `outbox_signal` readiness check returns `Healthy`.
- `outbox.signal.degraded` gauge returns to `0`.
- No new `Failed to start outbox signal listener` warnings in the last five minutes.

## Related

- `src/BuildingBlocks/Infrastructure/IntegrationEvents/IntegrationEventOutboxSignal.cs` — the LISTEN/NOTIFY listener.
- `src/BuildingBlocks/Infrastructure/IntegrationEvents/IntegrationEventOutboxHostedService.cs` — the hosted service that marks status on success/failure and continues polling in either case.
- `src/BuildingBlocks/Infrastructure/IntegrationEvents/OutboxSignalHealthCheck.cs` — the `IHealthCheck` implementation.
- `docs/TELEMETRY.md` — gauge definition.
- `docs/runbooks/outbox-dead-letter-recovery.md` — sister runbook for dispatch-time failures.
