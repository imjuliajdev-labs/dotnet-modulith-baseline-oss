# Migration Rollback Procedures

## EF Core Modules

EF Core modules support automated rollback via the DbMigrator CLI:

```bash
dotnet run --project src/Tools/DbMigrator -- --rollback-to <migration-name>
```

This invokes EF Core's `MigrateAsync(targetMigration)` which runs the `Down()` methods of all migrations after the target.

### Pre-rollback checklist

1. **Take a database backup** before any rollback in production:
   ```bash
   pg_dump -h <host> -U <user> -d baseline > baseline_backup_$(date +%Y%m%d_%H%M%S).sql
   ```
2. Stop the API host to prevent new requests during rollback
3. Identify the target migration name from the module's `Migrations/` folder
4. Run the rollback
5. Verify the schema state
6. Restart the API host

### Example

To roll back the Blog module to a specific migration:
```bash
dotnet run --project src/Tools/DbMigrator -- --rollback-to 20240101000000_InitialBlogSchema
```

## Durable rollback assessments for contract migrations

Contract migrations that intentionally throw from `Down()` keep their durable rollback assessment in this directory so operators and adopters can follow the supported recovery path without relying on local-only notes.

Current assessments:

- [`blog-contract-legacy-pascalcase-columns.md`](blog-contract-legacy-pascalcase-columns.md) — why `20260413004835_ContractBlogPostLegacyPascalCaseColumns` is forward-only and when restore-from-backup is required.

## Raw-SQL Modules (e.g., KnowledgeBase)

Raw-SQL modules using `IDatabaseMigration` do not have automated rollback because their migrations are inherently module-specific and may not be reversible.

### Manual rollback procedure

1. Take a database backup (see above)
2. Identify the SQL statements needed to reverse the migration
3. Connect to the database and execute the reversal SQL manually
4. Verify the schema state

### Module-specific rollback scripts

If a module needs a checked-in manual rollback script for a non-EF migration, keep it in this directory beside the matching assessment doc so the operator procedure and the SQL artifact live together.
