# EF Module Persistence Guide

Use EF Core by default for new module-owned relational persistence in this baseline.

The scaffold now generates an EF-ready `Persistence` folder for every new module so the default path is explicit instead of implied.

## Start Here

For a newly scaffolded module, begin in `src/Modules/{Module}/{Module}.Infrastructure/Persistence/` and work from these files in order:

1. `{Module}PersistenceDefaults.cs`
2. `{Module}PersistenceDbContext.cs`
3. `{Module}EntityTypeConfigurationExample.cs`
4. `{Module}PersistenceOptionsExtensions.cs`
5. `{Module}PersistenceDbContextFactory.cs`
6. `{Module}DatabaseMigration.cs`

Use these existing module references when you need a concrete example:

- Smallest EF module: [../src/Modules/SampleFeature/SampleFeature.Infrastructure/Persistence/SampleFeaturePersistenceDbContext.cs](../src/Modules/SampleFeature/SampleFeature.Infrastructure/Persistence/SampleFeaturePersistenceDbContext.cs)
- First production-grade EF teaching slice: [../src/Modules/Blog/Blog.Infrastructure/Persistence/BlogPersistenceDbContext.cs](../src/Modules/Blog/Blog.Infrastructure/Persistence/BlogPersistenceDbContext.cs)
- EF Core Npgsql provider and migrations history table wiring: [../src/Modules/Platform/Platform.Infrastructure/Persistence/PlatformPersistenceOptionsExtensions.cs](../src/Modules/Platform/Platform.Infrastructure/Persistence/PlatformPersistenceOptionsExtensions.cs)
- Specialized raw SQL pattern, not the default baseline: [../src/Modules/KnowledgeBase/KnowledgeBase.Infrastructure/Persistence/PostgresKnowledgeBaseStore.cs](../src/Modules/KnowledgeBase/KnowledgeBase.Infrastructure/Persistence/PostgresKnowledgeBaseStore.cs)

## What The Scaffold Gives You

The generated persistence shell is intentionally small:

- `PersistenceDefaults` anchors the module schema and migrations history table.
- `PersistenceDbContext` sets the module schema and gives you the place to apply real mappings explicitly.
- `EntityTypeConfigurationExample` shows the intended EF mapping shape with a persistence-only record name, without adding a live table by default.
- `PersistenceOptionsExtensions` centralizes Npgsql provider wiring. Both EF- and Dapper-style modules obtain their connection through the shared `IPostgresDataSourceResolver`; see [`adr/ADR-POSTGRES-DATA-SOURCE-RESOLVER.md`](adr/ADR-POSTGRES-DATA-SOURCE-RESOLVER.md).
- `PersistenceDbContextFactory` supports design-time migrations.
- `DatabaseMigration` gives the runtime migration runner a module-owned EF anchor.

The scaffold does not call `ApplyConfigurationsFromAssembly(...)` by default. The first real mapping should be wired with an explicit `modelBuilder.ApplyConfiguration(...)` call so persistence onboarding stays deliberate.

## First Real Mapping

When the module gets its first persisted aggregate or projection:

1. Replace `{Module}EntityTypeConfigurationExample.cs` with a real persistence record and matching `IEntityTypeConfiguration<T>`.
2. Keep persistence record names infrastructure-owned, such as `BlogPostRecord` or `PublishedBlogPostProjectionRecord`, especially when the relational shape diverges from the domain type.
3. Add explicit `modelBuilder.ApplyConfiguration(new YourPersistenceConfiguration())` calls in `{Module}PersistenceDbContext.cs` when the first real mapping is ready.
4. Add optimistic concurrency tokens where concurrent writes matter.
5. Generate the first migration only after the real mapping replaces the scaffold example.
6. Keep the schema module-local and avoid cross-module foreign keys.

## When Raw SQL Is Justified

Raw SQL is still valid, but it should be a deliberate exception, not the starting point for a new module.

Good reasons to choose a specialized SQL adapter include:

- focused read models or projections where EF adds little value
- bulk operations or Postgres-specific features that EF does not express clearly
- measured hot paths where EF is the proven bottleneck
- infrastructure-heavy inbox, outbox, or coordination tables with tight SQL control requirements

Even in those cases:

- keep the adapter in `.Infrastructure`
- keep migrations and schema ownership explicit
- keep cross-module boundaries unchanged
- do not copy a raw SQL module as the baseline for a fresh scaffold

## Rule Of Thumb

If you are creating a new module and you do not already have a measured reason to avoid EF Core, stay with the scaffolded EF path.

### Query projection guardrail

The architecture tests enforce that read surfaces in module Infrastructure prefer `.Select()` projections over `.Include()` eager loading. This prevents full entity graphs from being loaded when only a subset of data is needed on query paths.

The governing test is `EfQueryProjectionGuardrailTests.ReadSurfaceFilesMustNotUseEagerLoadingInclude`. It structurally detects read surfaces by scanning for files that implement `IBlogPostReadQueries`-style interfaces, `IQueryHandler<,>`, `I*QueryService`, or `I*Reader`. Write-side stores that only serve command handlers for aggregate loading are excluded.

### Read vs. write helpers

When a module has both query handlers and command handlers operating on the same entity family, separate the read and write concerns at the Infrastructure layer:

- **Read queries** (`I{Entity}ReadQueries`) — implement with `.Select()` projections into a flat intermediate type, then map to the domain type in memory. Register as a dedicated class (e.g. `BlogPostReadQueries`) and wire query handlers to this interface.
- **Write store** (`I{Entity}Store`) — implement with `.Include()` for aggregate reconstitution before mutation. This is legitimate and is excluded from the projection guardrail because the file does not implement a read surface.

The Blog module demonstrates this split: `BlogPostReadQueries` serves the three query handlers with `.Select()` projections, while `BlogPostStore` serves command handlers with `.Include()` for aggregate loading. This separation makes the BP-035 expectation structural rather than relying on naming conventions.
