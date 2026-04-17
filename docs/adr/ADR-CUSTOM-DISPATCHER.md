# ADR: Custom Dispatcher Instead of MediatR

- Status: Accepted
- Date: 2026-04-07
- Owners: Baseline maintainers

## Context

The baseline needs a CQRS dispatcher to route commands, queries, and integration events through a governed request pipeline. MediatR is the most widely used .NET library for this purpose, but it is no longer a viable option for this baseline for several reasons:

1. **Core dispatch is baseline-owned.** The dispatcher is a foundational seam in this repo. The baseline prefers to own that seam directly rather than couple its core contracts and runtime behavior to an external mediator library.

2. **Pipeline ordering is explicit.** MediatR resolves pipeline behaviors through the DI container, which means behavior execution order depends on registration order. The baseline requires an explicit, stable, documented pipeline order for request context, exception mapping, cancellation, telemetry, module-state, authorization, idempotency, validation, and command transactions that does not change when a new behavior is registered.

3. **Hot-path performance.** MediatR's default dispatch path uses `MethodInfo.Invoke` via reflection. The baseline's dispatcher uses cached compiled invokers to keep hot-path invocation off reflection, which matters for a system designed to route every use case through the dispatcher.

4. **Contract surface ownership.** Depending on a third-party dispatcher means the baseline's core abstractions (`ICommand`, `IQuery`, `ICommandHandler`, etc.) are defined by an external library whose API can change between versions. Owning the contracts means the baseline controls its own extension points and can evolve them without waiting for upstream changes or dealing with breaking updates.

## Decision

The baseline uses a custom in-house dispatcher subsystem.

This is a baseline-level architecture choice for this repo's governance and runtime-control goals. It is not a claim that MediatR is a poor choice for every modular monolith.

Canonical entry points:

- `BuildingBlocks.Application.Dispatching.DispatcherServiceCollectionExtensions.AddDispatcher(...)`
- `IDispatcher`
- `IIntegrationEventDispatcher`
- the `Dispatcher` and `IntegrationEventDispatcher` implementations under `BuildingBlocks.Application.Dispatching`

Subsystem invariants:

- first-class contracts for commands, queries, integration events, pipeline behaviors, validators, and exception handling are defined in `BuildingBlocks.Application`
- dispatcher composition stays on one canonical registration path through `AddDispatcher(...)`
- handler and integration-event invocation use cached compiled invokers instead of `MethodInfo.Invoke`
- pipeline behavior order is explicit and stable, defined in code rather than derived from container registration
- source-generated dispatch remains an optional future optimization, not a standing requirement

Regression expectations:

- architecture tests guard canonical registration and built-in pipeline order
- shared-abstraction tests guard the subsystem's baseline-owned public surface and hot-path invocation posture
- unit and integration tests cover handler discovery, exception translation, idempotency, authorization, and telemetry behavior

## Cancellation contract

`RequestHandlerDelegate<TResponse>` accepts a `CancellationToken` parameter. Each pipeline behavior owns the token it passes to `next`: behaviors that have no opinion forward the inbound token unchanged, while behaviors that need to bound a downstream stage substitute a linked token. The dispatcher does not close over the caller token when building the `next` chain; the token used inside the pipeline is whichever one the immediately surrounding behavior chooses.

The reference application of this contract is `RequestTimeoutBehavior`. It creates a linked `CancellationTokenSource`, calls `CancelAfter(timeout)`, and invokes `next(linkedCts.Token)`. When the timeout fires, the linked token cancels, the handler observes cancellation through the same token contract every other handler uses, and orphaned work after the timeout boundary cannot continue silently. When the original caller token is the one that fires, the OperationCanceledException propagates to the caller unchanged so timeout failures and caller cancellations remain distinguishable.

The convention is enforced executably: an architecture test (`DispatcherCancellationContractTests`) scans every file under `src/BuildingBlocks/Application/Dispatching/Behaviors/` for `next()` invocations with no arguments and fails the build if any are found. A new behavior that ignores the token by writing `await next()` will not compile against the new delegate signature, and even if a contributor introduces an overload-style shim the architecture test fails before merge.

## Consequences

- The baseline has zero dependency on MediatR or any commercial CQRS library.
- Pipeline ordering is deterministic, documented in the architecture docs, and enforced through architecture tests.
- New behaviors are added at a specific position in the pipeline, not appended to a container-ordered list.
- Contributors must learn the baseline's dispatcher contracts rather than relying on MediatR familiarity, but the contracts are intentionally similar in shape.
- The baseline is responsible for maintaining and testing its own dispatcher subsystem, including handler resolution, pipeline execution, registration invariants, and exception translation.
