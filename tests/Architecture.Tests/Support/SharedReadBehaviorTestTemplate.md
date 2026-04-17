# Shared Read Adapter Behavior Test Template

Every shared read adapter under `src/Modules/<Module>/<Module>.Application/SharedReads/` that declares a `SharedReadPolicy` **must** ship with a runtime behavior test fixture in `tests/Integration.Tests/SharedRuntime/`. Source-text guardrails (`SharedReadPolicyGuardrailTests`) only prove the policy object is present; they do not prove the adapter honors it at runtime.

The coverage architecture test `SharedReadBehaviorTestCoverageTests` enforces that every adapter source file is referenced by at least one test file under `tests/Integration.Tests/SharedRuntime/`.

## Required minimum shape

A behavior test fixture for an adapter `FooBarReader` must cover all three of the following:

### 1. Declared policy object

Load the static `Policy` field (via `SharedReadPolicyAccessor.ReadDeclaredPolicy(typeof(FooBarReader))`) and assert its `Name`, `Timeout`, and `FallbackBehavior` match the values the adapter promises. This prevents silent edits that relax the declaration without review.

### 2. Timeout path exercised

Construct the adapter with a fake upstream that blocks longer than `Policy.Timeout`. Assert:

- The adapter returns its declared fallback outcome (`Result<T>.Failure` with the declared error code for `SharedReadFallbackBehavior.Fail`, or the declared empty/default payload for `SharedReadFallbackBehavior.ReturnFallback`).
- The adapter does **not** leak `OperationCanceledException` / `TaskCanceledException` to the caller.
- The stopwatch elapsed time stays well under the sub-5s test budget.

### 3. Fallback path exercised

Construct the adapter with a fake upstream that throws a transient exception (`HttpRequestException`, `TimeoutException`, or similar). Assert the adapter returns the same declared fallback outcome as the timeout path. The caller must never observe a raw exception for any transient upstream failure.

## Preferred test shape

- Direct construction of the adapter with a hand-rolled fake is acceptable and preferred when the adapter has no DB/state dependencies — a `PostgresBackedApiApplication` boot would dominate the wall clock.
- Place the fixture in `tests/Integration.Tests/SharedRuntime/` so the coverage test can find it. File name should embed the adapter class name (e.g. `BlogIdentityTimeZoneReaderBehaviorTests.cs`) or the test file must otherwise reference the adapter class by name.
- Add a one-line comment at the top of the file explaining why direct construction was chosen over full DI.

## See also

- `tests/Integration.Tests/SharedRuntime/SharedReadTimeoutBehaviorTests.cs` — reference implementation of the timeout path.
- `tests/Integration.Tests/SharedRuntime/SharedReadFallbackBehaviorTests.cs` — reference implementation of the transient-exception fallback path.
- `tests/Architecture.Tests/SharedReadBehaviorTestCoverageTests.cs` — the coverage gate that enforces this template.
