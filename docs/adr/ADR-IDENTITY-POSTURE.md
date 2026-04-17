# ADR: Identity Module Posture

- Status: Accepted
- Date: 2026-04-11
- Owners: Baseline maintainers

## Superseded from

This ADR replaces the earlier dev-baseline posture recorded under the same file name (dated 2026-04-04). The previous posture described `Identity` as a development and test bootstrap baseline intended to be replaced by adopters. That wording created adoption ambiguity and drove a parallel "hardening plan" document that is now removed. The module is production-grade by default.

## Context

The previous posture declared the Identity module a baseline that adopters were expected to replace with a production-grade identity provider before handling real credentials. In practice this created three problems:

1. The module is substantial, polished, and broadly useful, which made the "replace it before production" guidance easy to ignore.
2. A separate `IDENTITY_HARDENING_PLAN.md` document existed only because Identity was framed as a baseline being hardened toward production, not as a production answer in its own right.
3. A custom `PermissionCatalog` / `PermissionDefinition` / `PermissionSnapshotVersion` authorization stack duplicated the standard ASP.NET Core Identity primitives (roles, claims, security stamp) and added maintenance cost without improving security posture.

Phases 1-5 of the Identity refactor resolved all three by adopting standard ASP.NET Core Identity with EF Core persistence, cookie authentication, role-based authorization, and ASP.NET Core Identity's built-in security stamp as the session invalidation primitive.

## Decision

The `Identity` module is a **production-grade implementation built on ASP.NET Core Identity, cookie authentication, and EF Core**, behind the existing `Identity` module boundary. It is not a replaceable adapter over an external provider. The provided infrastructure is the supported production answer.

Teams that require a different identity provider may replace `Identity.Infrastructure` with their own implementation, but the baseline ships a working production answer rather than a stub.

### Authorization model

Role-based authorization. Three built-in roles:

- `Admin`
- `User`
- `Machine`

Roles are the first-class authorization primitive. Commands and queries declare `RoleRequirement` entries in their `AuthorizationRequirements` collection; the dispatcher authorization pipeline enforces them centrally. Endpoint-level `[Authorize]` attributes supplement dispatcher-level checks at the API edge.

There is no custom `PermissionCatalog`, `PermissionDefinition`, `IPermissionDefinitionProvider`, or per-module `{Module}Permissions` class. Teams that need finer-grained authorization than three roles can add ASP.NET Core `IAuthorizationPolicy` policies or their own claim-based checks on top, but the baseline does not ship with that complexity.

### Authentication schemes

- **Browsers.** Cookie authentication. Cookie name `__Host-dotnet-modulith-baseline`, HttpOnly, Secure=Always, SameSite=Lax, 8h sliding expiration. Antiforgery is required for state-changing requests.
- **Service-to-service.** A separate `Machine` scheme over the `X-Machine-Key: {clientId}:{secret}` header. Machine clients never reuse the browser cookie path.

### Persistence

Standard ASP.NET Core Identity tables are adapted to the `identity` schema with snake_case column naming:

- `identity.accounts` (users)
- `identity.account_claims`
- `identity.account_logins`
- `identity.account_tokens`
- `identity.roles`
- `identity.account_roles`
- `identity.role_claims`
- `identity.machine_clients`

Machine clients have their own persisted table with hashed secrets and role assignments that draw from the same `IdentityRole` pool as users.

### Session invalidation

Session invalidation is driven by ASP.NET Core Identity's security stamp. Role changes, password resets, and admin-triggered `revoke-sessions` all bump the stamp through `UserManager.UpdateSecurityStampAsync`. There is no custom `PermissionSnapshotVersion` mechanism.

### Password policy

- minimum length 12 characters
- at least one digit
- at least one lowercase character
- at least one uppercase character
- at least one non-alphanumeric character
- lockout after 5 failed attempts for a default 5-minute window

### Bootstrap credentials

Seeded admin and seeded machine credentials are **Development and Testing only**. Any non-Development / non-Testing environment fails fast at startup if the configured seeded credentials exist. There is no override flag. Production deployments must provision real admin users through the user administration endpoints once an operator has signed in using a Development-only seed or through the first-run procedure documented in `docs/ADOPT.md`.

### Machine clients

Machine clients are persisted in `identity.machine_clients` with:

- hashed secrets
- full lifecycle: create, list, get, rotate secret, disable, reactivate, revoke
- role assignments drawn from the `IdentityRole` pool (typically `Machine`, optionally `Admin`)

The legacy seeded simple-string machine credential dual path is gone. There is one persisted machine credential path.

### Self-demotion guard

`SetIdentityUserRolesCommand` rejects any caller attempting to remove their own `Admin` role. This is enforced in the command handler, not in UI logic.

### Dispatcher routing

Every Identity endpoint routes through the module dispatcher. `MapIdentityApi` from ASP.NET Core Identity is **not** used. This preserves consistency with the modulith's module-execution pattern and ensures that authorization, idempotency, validation, module-state, and telemetry behaviors all apply to Identity requests the same way they apply to any other module's requests.

Notable endpoints:

- `POST /session/password` — user changes own password, requires recent authentication
- `POST /users/{actorId}/unlock` — admin clears lockout
- `PUT /users/{actorId}/roles` — admin replaces a user's role assignments (replaces the old `/permissions` endpoint)

## Canonical entry points

- `Identity.Api.IdentityModule`
- `Identity.Application.Authentication.*` (sign-in, step-up, password change)
- `Identity.Application.Administration.*` (user admin, role admin, machine-client admin)
- `Identity.Infrastructure.Persistence.IdentityDatabaseMigration` (seeds `IdentityRole` rows)
- `IdentityRoles` constants
- `RoleRequirement` (in `BuildingBlocks.Application.Authorization`)

## Regression expectations

The following tests enforce this posture and are required to stay green:

- `IdentityAuthenticationIntegrationTests`
- `IdentityUserAdministrationIntegrationTests`
- `IdentityBootstrapCredentialSafetyIntegrationTests`
- `IdentityBrowserCookieCompatibilityIntegrationTests`
- `MachineAuthenticationIntegrationTests`
- `IdentityPasswordChangeIntegrationTests`
- `IdentityRecentAuthenticationIntegrationTests`

## Consequences

- `IDENTITY_HARDENING_PLAN.md` is deleted. The hardening plan existed because Identity was explicitly a baseline being hardened toward production. Identity is now production-grade; there is no hardening plan.
- `IdentityReplaceabilityGuardrailTests` is deleted. Replaceability was an artifact of the baseline framing.
- The custom `PermissionCatalog` / `PermissionDefinition` / `IPermissionDefinitionProvider` infrastructure is removed. Module authorization is role-based only.
- The `PermissionSnapshotVersion` field and its associated cookie-invalidation logic are replaced by ASP.NET Core Identity's standard security stamp.
- `Identity.Domain` is currently minimal and retained to preserve the standard five-project module shape.
- The `AllowUnsafeSeededCredentialsInNonDevelopment` override flag is deleted. There is no way to ship seeded credentials into a non-development environment.
- Authorization is coarser-grained by default: three roles rather than a module-by-module permission namespace. Teams that need finer granularity can add policies on top, but the baseline does not ship with that complexity.
- The Identity posture is no longer an adoption ambiguity. The module is a module boundary around a production-grade implementation, not a module boundary around a stub.
