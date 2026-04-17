# ADR: No Shared Domain Event Abstraction By Default

- Status: Accepted
- Date: 2026-04-07
- Owners: Baseline maintainers

## Context

Many DDD-influenced architectures distinguish between two kinds of events:

- **Domain events** are raised within an aggregate or module and handled synchronously or near-synchronously within the same bounded context. They are typically used for intra-module side effects like updating projections, sending notifications, or triggering secondary writes after a primary aggregate change.
- **Integration events** cross module boundaries and are delivered asynchronously with at-least-once semantics.

Frameworks like MediatR popularized an `INotification` / `INotificationHandler` pattern where domain events are published through the mediator and handled by any registered handler, often in the same transaction. This is convenient but introduces hidden coupling: a handler registered against a domain event can silently add side effects to any command that raises that event, making the command's behavior dependent on the handler registration set.

## Decision

The baseline does not define a shared domain event abstraction. There is no shared `IDomainEvent`, no shared `IDomainEventHandler`, and no baseline-provided in-transaction event dispatcher for intra-module reactions.

This is the shared default for this baseline. It is not a claim that module-local domain events are always wrong, only that the baseline does not standardize them as a shared platform pattern.

Cross-module communication uses integration events only, published through the outbox after transaction commit.

The supported default for intra-module reactions is explicit orchestration inside the owning module's handlers or application services.

If a module's complexity genuinely warrants a local domain-event mechanism, that mechanism remains module-owned and does not become a shared baseline abstraction.

## Rationale

1. **Explicit over implicit.** When a command handler needs to update a projection, send a notification, or trigger a secondary write within the same module, that logic belongs in the handler or in an explicit application service that the handler calls. The control flow is visible in the code. With domain events, the same logic is scattered across handlers registered elsewhere, and the developer must search the container to understand what a command actually does.

2. **AI-generated code drifts on implicit patterns.** Domain event handlers are a common source of AI hallucination. An AI agent asked to "add a side effect when X happens" will happily register a new domain event handler without understanding the transactional boundary, the existing handler set, or whether the event is even raised in the expected context. Explicit orchestration is harder to misplace.

3. **Transaction boundary clarity.** Domain events dispatched within a transaction expand the transaction's scope invisibly. A new handler can add database writes, external calls, or long-running operations inside a transaction that was originally scoped to a single aggregate save. With explicit orchestration, the transaction boundary is visible in the handler.

4. **Integration events already cover cross-module needs.** The outbox pattern handles the "something happened, other modules should know" case with at-least-once delivery and replay safety. Domain events would only cover the intra-module case, which is better served by direct calls.

5. **Simplicity at the shared layer.** One shared event model (integration events) with one shared delivery mechanism (outbox) and one shared consumption pattern (inbox or dedupe) is easier to teach, test, and govern than providing both a shared domain-event system and a shared integration-event system.

## Consequences

- Intra-module side effects are explicit method calls or service invocations within the handler, not implicit event subscriptions.
- Contributors cannot register a handler that silently reacts to an internal state change in another handler. If a reaction is needed, it must be wired explicitly.
- Modules that need to notify other modules use integration events published through the outbox after commit, not synchronous in-transaction dispatch.
- This approach requires slightly more code in handlers that have multiple side effects (explicit calls instead of raising an event), but the tradeoff is full visibility of what a command does by reading its handler.
- If an adopter's domain genuinely benefits from intra-module domain events (for example, complex aggregate graphs with many loosely coupled reactions), they can add a module-local domain event mechanism within that module's boundary. The baseline does not provide or encourage a shared abstraction for it.
