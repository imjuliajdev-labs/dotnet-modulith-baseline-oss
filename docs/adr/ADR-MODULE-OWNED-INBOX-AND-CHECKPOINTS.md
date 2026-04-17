# ADR: Module-Owned Integration Event Inbox And Process Manager Checkpoints

- Status: Accepted
- Date: 2026-04-14
- Owners: Baseline maintainers

## Context

The baseline's `BLUEPRINT.md` is explicit on two rules:

- line 704: "process-manager state lives in the owning module schema"
- line 795: "inbox records live in the owning module schema"

For a long window the implementation violated both. `BuildingBlocks.Infrastructure.ServiceCollectionExtensions` registered
a single process-wide `IIntegrationEventInboxStore` and `IProcessManagerCheckpointStore`, each backed by
`PostgresIntegrationEventInboxStore` / `PostgresProcessManagerCheckpointStore`, each writing to
`starter_runtime.building_blocks_integration_event_inbox` and
`starter_runtime.building_blocks_process_manager_checkpoints` respectively. Both tables carried a `module_key`
discriminator column to separate records by consuming module, but the tables themselves lived in a shared
schema owned by no module.

That shape conflicted with the blueprint in three ways:

1. A table with a `module_key` column is not "module-owned"; it is a shared runtime table keyed by module.
   If SampleFeature were deleted from the baseline, the `admin.building_blocks_integration_event_inbox` rows
   for SampleFeature-sourced events would still sit in the shared schema instead of disappearing with
   SampleFeature.
2. The outbox side of the same subsystem was already per-module: `PostgresModuleIntegrationEventOutboxStore`
   takes `(moduleKey, schemaName, tableName)` at construction, ships with its own `IDatabaseMigration` for
   its table, and is registered by each publishing module via
   `AddPostgresIntegrationEventOutbox`. The inbox and checkpoint sides were asymmetric with the outbox
   side of the same subsystem — two halves of one architectural pattern, one half correct and one half not.
3. The "shared runtime table" approach blocked any future work on same-transaction commits between a
   handler's side effects and its inbox row (BLUEPRINT line 797: *a message is marked processed only after
   the handler's side effects commit*), because the inbox connection was structurally separate from any
   module's unit of work.

## Decision

Both stores become per-module, following the outbox subsystem's existing pattern exactly:

- `PostgresIntegrationEventInboxStore` takes `(IConfiguration, moduleKey, schemaName, tableName, connectionStringName)` at
  construction, serves exactly one module, asserts the incoming `IntegrationEventDeliveryContext.ModuleKey`
  matches its own on every call, and also implements `IDatabaseMigration` so its own table is created under
  its own schema. Default table name is `integration_event_inbox`; the `building_blocks_` prefix is gone.
- `PostgresProcessManagerCheckpointStore` takes the same constructor shape, asserts the `moduleKey`
  parameter on every call matches its own, and implements `IDatabaseMigration` for its own
  `process_manager_checkpoints` table under the module schema.
- The primary key on the inbox table is `(consumer_name, event_id)` instead of
  `(module_key, consumer_name, event_id)`. The `module_key` column is gone — the table IS the module's
  inbox, so every row belongs to the owning module.
- The primary key on the checkpoint table is `(process_manager_name, process_id)` instead of
  `(module_key, process_manager_name, process_id)`. Same reasoning.
- `IIntegrationEventInboxStore` and `IProcessManagerCheckpointStore` both expose a `string ModuleKey { get; }`
  property, matching `IIntegrationEventOutboxStore`.
- `IntegrationEventDispatcher` now takes `IEnumerable<IIntegrationEventInboxStore>` instead of a single
  singleton, builds a dictionary by `ModuleKey` at construction, and routes each
  `IntegrationEventDeliveryContext` to the consuming module's own inbox store. If no store is registered
  for the consuming module, the dispatcher fails fast with an actionable error naming the helper the
  module should call.
- `BuildingBlocks.Infrastructure.ServiceCollectionExtensions` no longer registers default singletons for
  either store. Each consuming module registers its own via the new
  `services.AddPostgresIntegrationEventInbox(moduleKey, schemaName)` and
  `services.AddPostgresProcessManagerCheckpoints(moduleKey, schemaName)` helpers, which mirror the existing
  `AddPostgresIntegrationEventOutbox` helper in file, shape, and registration surface.
- The inbox and checkpoint table creation is removed from `SharedRuntimePersistenceDatabaseMigration`. The
  corresponding table-name constants are removed from `SharedRuntimePersistenceDefaults`.

Admin is the only module in the baseline that actually consumes integration events or runs a process
manager, so the real adopter-visible change is the addition of `admin.integration_event_inbox` and
`admin.process_manager_checkpoints` under the `admin` schema, registered in
`AdminInfrastructureServiceCollectionExtensions`.

Two adjacent tables in `starter_runtime` are left in place on purpose:

- `building_blocks_data_protection_keys` stays because the ASP.NET Core Data Protection key ring is a
  genuinely cross-cutting concern shared across modules, already governed by
  `ADR-SHARED-RUNTIME-NO-FALLBACK-BASELINE.md`.
- `building_blocks_command_idempotency` stays because the dispatcher's command idempotency is a
  dispatcher-owned technical facility, not a module's business state. It sits next to the dispatcher's
  other cross-cutting primitives and is managed by
  `CommandIdempotencyRetentionHostedService` at the runtime level.

An architecture test (`NoSharedRuntimeInboxOrCheckpointLiterals` in
`SharedAbstractionTests`) scans every source file under `src/` and fails if any reference to
`starter_runtime.building_blocks_integration_event_inbox` or
`starter_runtime.building_blocks_process_manager_checkpoints` reappears.

## Rationale

1. **Blueprint-faithful.** Every record that `BLUEPRINT.md:704` and `:795` describe as "lives in the
   owning module schema" now actually does.
2. **Symmetric with the outbox side.** The subsystem is a single pattern now: outbox, inbox, and process
   manager checkpoints all live in module schemas, register via per-module helpers, and implement
   `IDatabaseMigration` themselves. Future contributors only have to learn one shape.
3. **No shims.** The shared `Postgres*Store` classes are rewritten in place, not marked obsolete and
   kept around. There is no dual path. Every module either registers its own stores or does not use the
   subsystem at all.
4. **Unblocks same-transaction commits.** The store constructors now take the schema at registration
   time. A follow-up change can extend the helpers to accept a `DbContext` or unit-of-work scope and
   thread the commits into the module's own transaction, satisfying `BLUEPRINT.md:797` fully. That
   follow-up is out of scope for this ADR.
5. **Failure mode is explicit.** If a module exposes an integration event handler but forgets to register
   its inbox, the dispatcher fails loudly at the first event with a clear message naming the helper to
   call. The previous shared-singleton shape silently swept the record into `starter_runtime`.
6. **Test harness is honest.** Integration tests that use fake consuming modules (`reports`, `accounts`,
   `chaos`, `publishing`, `blog`, `knowledge-base`, `sample-feature`) each register their own inbox
   explicitly. The harness also threads a `configureExtraMigrations` callback through
   `PostgresBackedApiApplication.StartAsync` so the pre-host `DbMigrator`-equivalent step can create the
   inbox tables for those fake modules before the host starts.

## Consequences

- Adopters who fork the baseline as-is get the corrected shape automatically.
- Adopters who forked before this ADR and want to migrate must: add per-consuming-module calls to
  `AddPostgresIntegrationEventInbox(moduleKey, schemaName)` and
  `AddPostgresProcessManagerCheckpoints(moduleKey, schemaName)` in each consuming module's infrastructure
  extension, run the new `IDatabaseMigration` entries to create the per-module tables, and backfill data
  from `starter_runtime.building_blocks_integration_event_inbox` / `_process_manager_checkpoints` into
  the new per-module tables if they have in-flight records. A `DROP TABLE` on the old `starter_runtime`
  tables then completes the cut.
- The BuildingBlocks.Infrastructure LOC budget was raised from 4293 to 4450 and the file budget from 62
  to 64 to admit the two new registration helpers and the migration support code on the per-module
  stores. This is genuine shared-kernel work — each module would otherwise need to hand-write the same
  SQL and the same registration plumbing — and collapses duplicate paths into one canonical mechanism,
  which is the admission condition in `BLUEPRINT.md` for BuildingBlocks growth (BP-005).
- `BLUEPRINT.md` language around BP-017 (process-manager state) and the integration-event inbox rule
  (BP-020 adjacent) is updated to reference this ADR and the new enforcement path.
- The `RULE_TO_GATE_CATALOG.md` entries for BP-017 and BP-019 / BP-020 now name the new architecture
  test (`NoSharedRuntimeInboxOrCheckpointLiterals`) as an additional enforcement artifact alongside the
  existing integration tests.

## Out Of Scope

- Same-transaction commit of handler side effects with inbox row updates. The store shape now admits it,
  but the implementation requires threading `DbContext` or a unit-of-work scope into the store. It is a
  separate reviewed change.
- Migrating `building_blocks_command_idempotency` out of `starter_runtime`. The current answer is "it
  stays, because it is dispatcher-owned." A future decision to per-module-ify it would be its own ADR.
