# ADR: Shared Runtime No-Fallback Baseline

- Status: Accepted
- Date: 2026-04-06
- Owners: Baseline maintainers

## Context

The baseline's shared runtime previously changed durability semantics silently when `ConnectionStrings:BaselineDatabase` was absent.

- `AddBuildingBlocksInfrastructureDefaults()` selected in-memory implementations for idempotency, inbox, and process-manager checkpoints when no database connection string was configured.
- `Start-ApiHost.ps1` allowed the host to start without running DbMigrator when the database connection string was absent.
- The supported bootstrap smoke already required PostgreSQL, while the default shared-runtime path still permitted a degraded local mode.

That split posture conflicted with the repo's industrialized end-state target and made the supported runtime path less strict than the documented architecture implied.

## Decision

The supported composed runtime has a single strict shared-runtime posture.

1. `ConnectionStrings:BaselineDatabase` is required for the shared-runtime durability path.
2. `AddBuildingBlocksInfrastructureDefaults()` does not silently register in-memory durability stores on the default runtime path.
3. Missing database configuration is a startup misconfiguration and must fail clearly.
4. In-memory durability implementations may remain in the repository for explicit test harnesses or other intentional non-default composition paths, but they are not part of the supported composed runtime baseline.

## Consequences

- The default runtime path now matches the governed architecture more closely.
- Durability semantics no longer change based on missing configuration.
- The supported bootstrap path is simpler: configure the database, run migrations, then start the host.
- Tests that intentionally compose a reduced runtime surface must now do so explicitly instead of relying on silent fallback behavior.
