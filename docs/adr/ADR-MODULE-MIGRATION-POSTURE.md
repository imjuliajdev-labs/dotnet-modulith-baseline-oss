# ADR: Module Migration Posture

- Status: Accepted
- Date: 2026-04-05
- Owners: Baseline maintainers

## Context

This baseline already has one supported migration execution path:

- `src/Tools/DbMigrator/Program.cs` resolves `IDatabaseMigrationRunner`
- `IDatabaseMigrationRunner` groups configured migrations by connection string and executes them under a PostgreSQL advisory lock
- ApiHost checks migration readiness at startup and fails fast when pending work exists

What can look inconsistent at first glance is the module implementation shape behind that runner.

- Platform owns an EF Core `DbContext`, model snapshot, and EF migrations.
- Shared runtime, outbox, inbox, and several store-backed modules currently implement `IDatabaseMigration` directly with explicit SQL.

Without a documented rationale, adopters can misread this as accidental drift or as two competing migration systems.

## Decision

The baseline's current supported default is a unified migration runner with two allowed implementation styles behind the `IDatabaseMigration` boundary.

1. Use EF Core migrations when a module owns a real `DbContext` and benefits from model snapshots and `MigrateAsync()`.
2. Use a module-owned `IDatabaseMigration` implementation with explicit SQL when the module currently exposes a store-backed adapter without a `DbContext`.
3. Register both styles through the same `IDatabaseMigration` surface so `DbMigrator`, readiness checks, and integration tests see one consistent migration contract.
4. Do not apply schema changes from ApiHost startup logic beyond readiness validation.

This means the repo has one migration execution model, not two. The implementation detail varies by module ownership shape, but the operational contract stays the same.

## Consequences

- The current mix of EF-backed and SQL-backed module migrations is intentional and supported.
- Expand-and-contract, schema isolation, and advisory-lock execution rules apply equally to both styles.
- A module can move from a direct SQL `IDatabaseMigration` implementation to EF Core migrations later if its persistence model grows into a `DbContext`-backed shape.
- Teams adopting the baseline should choose the migration style per module persistence design, but keep all schema changes behind `IDatabaseMigration` and `DbMigrator`.
- Review feedback about migration posture can now point to an explicit repo decision instead of inferred source behavior.

## Rollback Guidance

EF Core modules support automated rollback through `DbMigrator --rollback-to <migration-name>`. This invokes `MigrateAsync(targetMigration)` which runs `Down()` methods of all migrations after the target. Each EF Core module registers an `IDbContextRollbackRegistration` so the migrator can discover and roll back its `DbContext`.

Raw-SQL modules using `IDatabaseMigration` do not have automated rollback. Their rollback procedure is documented as a manual operational runbook in `docs/operations/rollback/`.

Pre-migration database backup (`pg_dump`) is the recommended operational practice before any rollback in production. See `docs/operations/rollback/README.md` for the full procedure.
