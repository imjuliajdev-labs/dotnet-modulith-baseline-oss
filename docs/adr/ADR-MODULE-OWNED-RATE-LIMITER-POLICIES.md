# ADR: Module-owned rate limiter policies

- Status: Accepted
- Date: 2026-04-14
- Owners: Baseline maintainers

## Context

Prior to this decision, `ApiHost/ApiHostComposition.cs` registered ASP.NET rate limiter policies on behalf of individual modules (Identity's `AuthenticationAttempt` and `PasswordMutation`, Platform's `ModuleStateMutations`). Doing so required the host to `using Identity.Api;` and `using Platform.Api;` so it could reach the policy-name constants. That coupled the composition root to specific modules, broke the "ApiHost is composition-only" rule, and meant that adding or removing a module's rate-limiter policy was not a local change inside that module.

## Decision

For this baseline, each module owns the registration of its own rate-limiter policies. A module that needs policies implements `IModuleRateLimiterContributor` from `BuildingBlocks.Infrastructure.Modules` and adds them via the standard options pipeline (`services.Configure<RateLimiterOptions>(...)` or `services.AddOptions<RateLimiterOptions>().Configure<IOptions<TModuleOptions>>((opts, moduleOpts) => ...)` when the policy depends on module configuration). `ApiModuleComposition.AddApiModule` invokes the contributor as part of the module registration path.

`ApiHost` still calls `AddRateLimiter(...)` — it keeps cross-cutting defaults such as `RejectionStatusCode` — but it no longer names any module. The rule is enforced structurally by `ApiHostMayNotImportModuleApiNamespaces` in `ApiEdgeGuardrailTests`.

Module-specific configuration for rate limiting uses the standard typed-options pattern with `ValidateOnStart` (e.g. `Platform.Infrastructure.Configuration.PlatformRateLimiterOptions`).

## Consequences

- Adding a rate-limited endpoint is a local change inside the owning module. The host does not change.
- `ApiHost` cannot regress into reaching into any module namespace without the architecture test failing.
- Modules carry the responsibility for validating their own rate-limiter options (via `ValidateOnStart`), keeping configuration failures localized to the module that owns them.
- The contributor interface is opt-in: modules without rate-limiter policies do not implement it and pay no cost.
