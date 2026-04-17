# ADR: Custom Outbox and Inbox Over Messaging Frameworks

- Status: Accepted
- Date: 2026-04-07
- Owners: Baseline maintainers

## Context

The baseline needs at-least-once integration event delivery between modules with outbox persistence (publisher side) and inbox deduplication (consumer side). Established .NET libraries exist for this purpose:

- **MassTransit** provides outbox, inbox, saga, and transport abstractions over RabbitMQ, Azure Service Bus, Amazon SQS, and an in-memory transport.
- **Wolverine** provides similar capabilities with a different API surface.
- **NServiceBus** is a mature commercial messaging framework.

All three are capable, but all three introduce tradeoffs that conflict with the baseline's design priorities.

## Decision

The baseline uses a custom PostgreSQL-backed integration-delivery subsystem. No external messaging framework or message broker is used.

This is a default for an in-process, PostgreSQL-backed modular monolith baseline. It is not a claim that broker-backed messaging frameworks are wrong in systems with different scale, topology, or operational constraints.

Canonical entry points:

- `AddPostgresIntegrationEventOutbox(...)`
- `IIntegrationEventOutboxPublisher`
- `IIntegrationEventOutboxDispatcher`
- `IntegrationEventOutboxHostedService`
- `IIntegrationEventInboxStore`

Subsystem invariants:

- each publishing module owns an `integration_outbox` table in its schema
- durable inbox or dedupe state is persisted in PostgreSQL and scoped by module and consumer
- outbox dispatch uses `FOR UPDATE SKIP LOCKED` for multi-instance safety
- events are dispatched in-process by a shared background pump, not through an external broker
- the shared dispatcher routes integration events to registered handlers within the same process
- retry, leasing, and dead-letter policy remain centralized in the subsystem rather than feature-local worker loops

## Rationale

1. **No external broker dependency.** The baseline is a modular monolith, not a distributed system. All modules run in the same process. Introducing RabbitMQ, Azure Service Bus, or SQS as a dependency for in-process module communication adds operational complexity, infrastructure cost, and a failure mode that does not exist without it.

2. **Commercial licensing.** NServiceBus is commercial (BP-025 prohibits commercial packages). MassTransit's core is open source, but its outbox implementation and some transports have licensing considerations that could change.

3. **Transparency over abstraction.** Messaging frameworks introduce their own conventions for serialization, routing, retry, dead-letter, and consumer lifecycle. When something goes wrong, debugging requires understanding the framework's internals. The custom outbox is ~200 lines of SQL and C# that any contributor can read, debug, and modify without framework expertise.

4. **PostgreSQL is already there.** The outbox pattern requires a transactional store that participates in the same transaction as the business write. Since every module already uses PostgreSQL, the outbox table lives in the same schema and commits in the same transaction. No two-phase commit, no distributed transaction coordinator, no additional infrastructure.

5. **Upgrade path is clear.** If the application eventually grows into a distributed system where modules are extracted into separate services, adding a message broker at that point is a deliberate architectural decision with real requirements (ordering, partitioning, throughput). The outbox table structure and event contracts translate directly to broker-based publishing. The custom implementation does not prevent future adoption of MassTransit or similar; it avoids premature adoption.

## Consequences

- The baseline has no dependency on MassTransit, Wolverine, NServiceBus, or any message broker.
- Integration events are delivered in-process only. Cross-process delivery requires adding a broker transport, which is out of scope for the baseline.
- The outbox and inbox or dedupe implementations are baseline-owned and tested directly. Contributors must understand the SQL-level mechanics rather than relying on framework documentation.
- Retry policy, dead-letter behavior, and batch size are configured in the baseline's own code, not in a framework's configuration model.
- Consumer ordering guarantees are explicit and narrow. The baseline does not assume global ordering across modules, which matches the at-least-once contract.
- Regression expectations are explicit: architecture tests guard canonical registration and pump wiring, while integration tests cover dispatch, replay safety, retry, dead-letter behavior, and bulkheads.
- If an adopter's scale or topology requires a real message broker, they replace the outbox dispatcher with a broker publisher and add consumer endpoints. The event contracts in `.PublicContracts` remain unchanged.
