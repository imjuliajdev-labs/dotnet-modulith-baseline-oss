# ADR: Postgres Data Source Resolver As The Shared Connection-Composition Seam

- Status: Accepted
- Date: 2026-04-16
- Owners: Baseline maintainers

## Context

Before this decision, every Postgres-touching store in the shared BuildingBlocks layer and in each module's Infrastructure project composed its own connection by calling `configuration.GetConnectionString("BaselineDatabase")` (or a module-specific key) and either opening a raw `NpgsqlConnection` or constructing an `NpgsqlDataSource` locally. That shape had three recurring costs:

- The same connection-string read appeared in many places, so any change to the resolution rules (multi-tenant keys, secret rotation, connection-string transformation) touched a large surface.
- Each call site owned its own `NpgsqlDataSource` lifecycle, which is the correct place to attach Npgsql interceptors, map type handlers, or tune pooling. Duplication made it impossible to land a single behavior change consistently.
- New modules tended to copy whatever their nearest reference module did, which drifted the shape over time.

The baseline already commits to PostgreSQL as the only database (see [ADR-POSTGRESQL-ONLY.md](ADR-POSTGRESQL-ONLY.md)) and to module-owned infrastructure tables (see [ADR-MODULE-OWNED-INBOX-AND-CHECKPOINTS.md](ADR-MODULE-OWNED-INBOX-AND-CHECKPOINTS.md)). Those decisions make a single shared connection-composition seam cheap to introduce and expensive to leave missing.

## Decision

For this baseline, Postgres connection composition is centralized behind one shared-layer abstraction:

- `IPostgresDataSourceResolver` in `src/BuildingBlocks/Infrastructure/Persistence/PostgresDataSourceResolver.cs` is the only supported way for production code to obtain a `NpgsqlDataSource` or to read a `ConnectionStrings:*` value.
- Each logical connection string name (for example `"BaselineDatabase"`) resolves to exactly one `NpgsqlDataSource` per host, built lazily and kept for the life of the resolver. The resolver owns that lifecycle and disposes the data source at shutdown.
- Every Postgres-backed store in BuildingBlocks and in each module's Infrastructure project takes `IPostgresDataSourceResolver` as a constructor dependency and opens connections through it. Raw `new NpgsqlConnection(connectionString)` construction in `src/**/*.cs` is not a supported pattern.

The rule is enforced by an existing architecture test so the baseline cannot drift back into ad hoc composition:

- `PostgresConnectionsAreComposedThroughTheSharedDataSourceResolver` in `tests/Architecture.Tests/SharedAbstractionTests.cs:145-158` scans every C# file under `src/` and fails the build if any file outside the resolver contains `new NpgsqlConnection(`.

The resolver interface is deliberately narrow (one getter for the connection string, one getter for the data source) so that future shared concerns like interceptor registration, OpenTelemetry instrumentation, or secret rotation can be added without widening every call site.

## Rationale

1. **One canonical read site for `ConnectionStrings:*`.** A future change to how the baseline resolves connection strings (environment variable precedence, per-module overrides, secret manager indirection) lands in one place instead of every store.

2. **`NpgsqlDataSource` ownership belongs to the container, not to the consumer.** Npgsql's guidance is that a `NpgsqlDataSource` should be built once and shared across consumers. Individual stores constructing their own would fragment pooling and make instrumentation impossible to apply uniformly.

3. **New modules cannot regress the shape by accident.** Scaffolded Infrastructure projects take `IPostgresDataSourceResolver` as a dependency the same way every existing store does, and the architecture test blocks any reintroduction of raw connection construction.

4. **Backward-compatible with the `"BaselineDatabase"` default.** The resolver does not replace the existing `ConnectionStrings:BaselineDatabase` key or the shared-runtime startup validation (`SharedRuntimePersistenceConfigurationValidationHostedService`). Modules that need a distinct database can add a new connection-string name and resolve it through the same interface.

5. **Minimal interface surface.** Only the two methods needed by current call sites are exposed. Anything broader (connection-string rewriting, health probes, connection-level interceptors) would be added as an explicit interface evolution, not through incidental extensions to a general-purpose type.

## Consequences

- Every Postgres-touching store now depends on `IPostgresDataSourceResolver`. Adopters replacing the resolver would need to reintroduce per-store data-source composition and accept the duplication that caused this decision.
- Tests and tools that need a real Postgres connection compose their own `IPostgresDataSourceResolver` through the same DI registration path used in `ApiHost`. The `ConfiguredPostgresDataSourceResolver` lives in BuildingBlocks.Infrastructure and is registered in the default composition.
- Future cross-cutting behaviors on connections (tracing, logging, retry policies at the connection layer) have a single seam to hook.
- Modules using specialized raw SQL stores (for example `PostgresKnowledgeBaseStore`) route through the resolver exactly like EF-backed modules; the `EF_MODULE_PERSISTENCE_GUIDE.md` "specialized raw SQL" pattern remains available without any relaxation of this rule.
- The enforcement test has no per-file waiver mechanism. Any future exception must either be admitted into the resolver implementation itself or ship with an explicit waiver entry in `waivers/active.waivers.json` plus a follow-up removal condition.

## Not Decided Here

- **EF Core vs. raw SQL for module persistence.** Covered by the broader default in [`../EF_MODULE_PERSISTENCE_GUIDE.md`](../EF_MODULE_PERSISTENCE_GUIDE.md): EF Core is the scaffolded default; raw SQL is a deliberate exception.
- **Connection-string naming conventions for new logical databases.** Out of scope; the resolver accepts any `ConnectionStrings:*` name and leaves naming to the module that needs a new database.
- **Connection-level instrumentation shape.** Not committed to in this ADR. When added, it lands inside the resolver as a single admission decision.
