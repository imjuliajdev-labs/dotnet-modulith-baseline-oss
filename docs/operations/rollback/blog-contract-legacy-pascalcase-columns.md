# Blog contract migration rollback assessment — legacy PascalCase columns

## Migration

- Module: `Blog`
- Migration: `20260413004835_ContractBlogPostLegacyPascalCaseColumns`

## What this migration removes

This contract migration removes the legacy Blog compatibility surface that existed during the expand-and-contract window:

- PascalCase columns on `blog.blog_posts`
- the sync trigger on `blog.blog_posts`
- the sync function that repopulated the legacy columns

After this migration, the supported schema is the snake_case-only shape.

## Why `Down()` is intentionally unsupported

An in-place `Down()` implementation would need to reconstruct data and compatibility behavior that are no longer safely derivable as a generic migration step:

1. recreate the dropped PascalCase columns
2. repopulate them from the current snake_case values
3. recreate the legacy sync function
4. recreate the legacy trigger
5. restore the old compatibility assumptions without knowing whether any downstream tooling still expects them

That is not a safe or deterministic rollback for a production contract migration. The migration therefore throws `NotSupportedException` from `Down()`.

## Supported recovery paths

### Before the contract migration has executed

Do **not** run the contract migration yet. Keep the compatibility window open and continue operating on the expand shape until the old application path and old consumers are gone.

### After the contract migration has executed in production

The supported recovery path is:

1. stop the rollout
2. restore a database backup taken **before** `20260413004835_ContractBlogPostLegacyPascalCaseColumns`
3. redeploy the application version that matches the pre-contract schema if required

If the issue is application-only and the pre-contract version is still compatible with the current schema, prefer **application rollback without schema rollback** per [`../../MIGRATION_ROLLBACK.md`](../../MIGRATION_ROLLBACK.md).

## Operator checklist

- confirm whether the failure is application behavior or schema shape
- confirm whether the contract migration has already executed in the affected environment
- identify the newest backup taken before the contract migration
- verify the restore window and downstream impact before restoring
- restore only if application rollback is no longer schema-compatible

## Related docs

- [`../../MIGRATION_ROLLBACK.md`](../../MIGRATION_ROLLBACK.md)
- [`README.md`](README.md)
