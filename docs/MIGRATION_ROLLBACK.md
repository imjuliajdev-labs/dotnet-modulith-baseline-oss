# Migration Rollback

This baseline uses **expand-and-contract** schema evolution (BP-020). Migrations are forward-only by intent: the production strategy is to roll the application back to a previous version that is compatible with the current schema, not to re-run a `Down()` script against a live database. This document explains the supported reversal mechanisms and the decision tree for each scenario.

## Supported reversal mechanisms

### 1. `DbMigrator --rollback-to <migration-id>` (development and pre-production)

The custom `DbMigrator` host exposes `--rollback-to` for emergency schema reversion in environments where it is acceptable to invoke `MigrationBuilder.Down()` automatically. Use this path only when:

- the target environment is **development**, **CI**, or a **disposable pre-production** stamp;
- every migration between the current head and the target migration has a **non-throwing `Down()`** implementation;
- no destructive contract migration sits between current and target.

If any migration in the range throws, the runner stops at that migration and leaves the database at an intermediate state. Restore from backup if this happens in shared environments.

### 2. Application rollback (production default)

In production, the supported reversal is to **deploy the previous application version** while leaving the schema at the current head. This works because expand-and-contract migrations are designed to keep the previous version functional during the compatibility window. Schema changes that **break the previous version** must be split:

1. **Expand** migration — adds new columns/tables, dual-writes if required, leaves old shape intact. Both old and new application versions can run.
2. Application rolls forward; new version exercises the new shape.
3. After the compatibility window closes, **contract** migration drops the old shape. From this point, the previous application version is no longer compatible — application rollback is no longer a valid recovery path for changes affecting the contracted columns.

The contract step is when rollback risk peaks. Confirm via integration tests, runbook, and observability that the new shape is healthy before contracting.

### 3. Restore from backup

Used when:

- a contract migration has already executed and the new version is broken;
- a migration with a throwing `Down()` is in the rollback range;
- multiple migrations need to be reverted simultaneously and the runner cannot guarantee atomicity.

This is the **only** supported recovery path past a destructive contract migration. The backup must predate the contract migration.

## Migrations that intentionally throw on `Down()`

Any migration that drops columns, drops tables, or removes a function/trigger should throw `NotSupportedException` from `Down()` with a message naming the contracted shape and pointing operators at the restore-from-backup procedure.

Current examples:

| Migration | Module | What it drops | Why `Down()` throws |
|---|---|---|---|
| `20260413004835_ContractBlogPostLegacyPascalCaseColumns` | Blog | Legacy PascalCase columns, sync trigger, sync function | Restoring the trigger and repopulating PascalCase columns from snake_case values cannot be done in-place safely. See [`docs/operations/rollback/blog-contract-legacy-pascalcase-columns.md`](operations/rollback/blog-contract-legacy-pascalcase-columns.md). |

## Decision tree

```
Need to revert a schema change?
├── Was the change an EXPAND migration (additive)?
│   └── Yes → roll the application back. Schema can stay at head.
│
├── Was the change a CONTRACT migration (destructive)?
│   ├── Already executed in production?
│   │   └── Restore from backup taken before the contract migration.
│   └── Not yet executed?
│       └── Pause the deploy. Reopen the compatibility window if needed.
│
└── Is this dev/CI/disposable pre-prod?
    └── Use DbMigrator --rollback-to. Inspect Down() of every migration in the range first.
```

## Authoring guidelines

When you add a migration, classify it explicitly in the PR description as **expand** or **contract**.

- **Expand** migrations must keep the previous application version functional. Dual-writes belong in the application layer until the contract step.
- **Contract** migrations must throw `NotSupportedException` from `Down()` and link a durable rollback assessment under [`docs/operations/rollback/`](operations/rollback/) that captures the supported recovery path.
- The `Modules.<Module>.Infrastructure.Persistence.Migrations` integration tests must cover both shapes when an expand-and-contract pair lands across PRs.

See also: [`docs/QUALITY_GATES.md`](QUALITY_GATES.md) Data Gate, [`docs/BLUEPRINT.md`](BLUEPRINT.md) BP-020.
