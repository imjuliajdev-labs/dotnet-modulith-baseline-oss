# ADR: PostgreSQL-Only, No Database Abstraction

- Status: Accepted
- Date: 2026-04-07
- Owners: Baseline maintainers

## Context

Many .NET baselines abstract the database behind a generic repository layer or use EF Core's provider model to support multiple databases (SQL Server, PostgreSQL, SQLite, etc.). This flexibility comes at a cost: the baseline cannot use database-specific features, migration tooling diverges across providers, and the test environment may behave differently from production if a lighter provider is used for testing.

## Decision

The baseline targets PostgreSQL exclusively. There is no database abstraction layer, no pluggable provider model, and no support for SQL Server, SQLite, or in-memory databases on the production path.

This is a posture decision for this baseline's governed default, not a claim that every modular monolith should be PostgreSQL-only.

Specific choices:

- The scaffolded and default relational module path uses EF Core with the Npgsql provider, while specialized modules may use direct PostgreSQL access when EF is not the best fit.
- Schema-per-module isolation uses PostgreSQL schemas.
- The migration runner uses PostgreSQL advisory locks for concurrency safety.
- Outbox dispatch uses `FOR UPDATE SKIP LOCKED`, a PostgreSQL-specific locking semantic.
- Integration tests use Testcontainers with a real PostgreSQL instance, not an in-memory substitute.
- NodaTime integration uses the Npgsql NodaTime plugin for native `timestamptz` and `date` mapping.

## Rationale

1. **One database, fully exercised.** Supporting multiple providers means testing against multiple providers, maintaining compatibility across SQL dialects, and avoiding database-specific features. A baseline that supports everything well supports nothing deeply.

2. **PostgreSQL-specific features matter.** Advisory locks, `FOR UPDATE SKIP LOCKED`, schema-level isolation, and native NodaTime type mapping are not available across all providers. Abstracting them away would mean building inferior alternatives or losing them entirely.

3. **Test-production parity.** Using the same database engine in integration tests (via Testcontainers) and production eliminates an entire class of bugs where tests pass against SQLite or in-memory but fail against the real engine.

4. **PostgreSQL is free and widely available.** Unlike SQL Server, PostgreSQL has no licensing cost, runs on every major cloud provider as a managed service, and is available as a lightweight Docker container for local development.

5. **Adopters who need SQL Server can still migrate.** For EF-backed modules, switching the Npgsql provider to the SQL Server provider is mechanically possible, but it is still the adopter's responsibility. Modules that use specialized PostgreSQL SQL require a deeper infrastructure rewrite. The baseline does not pretend to support that path and does not carry the abstractions that would make it seamless.

## Consequences

- The baseline has a hard dependency on PostgreSQL 16+.
- Contributors and adopters must have access to a PostgreSQL instance (local install or Docker).
- Database-specific SQL in migrations, outbox queries, and advisory locks is written directly against PostgreSQL, not behind an abstraction.
- Adopters who require a different database engine must replace EF-backed infrastructure where needed, rewrite PostgreSQL-specific SQL adapters, and replace the migration runner and outbox queries. This is a meaningful effort, not a configuration change.
- The Docker Compose setup includes PostgreSQL, so the barrier to entry for evaluation is `docker compose up`.
