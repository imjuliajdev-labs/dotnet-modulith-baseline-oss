# KnowledgeBase

This module is reference material, not scaffolding. Add new modules with [`../../../scripts/New-Module.ps1`](../../../scripts/New-Module.ps1) and [`../../../templates/module/module-spec.example.json`](../../../templates/module/module-spec.example.json).

## Purpose

`KnowledgeBase` is the richer end-user reference module. Use it when you want to study a fuller module with public reads, operator-managed content, runtime settings governance, idempotent publication, and a real browser-facing surface.

## What It Teaches

- public FAQ-style read endpoints and operator-managed write endpoints in [`KnowledgeBase.Api/KnowledgeBaseModule.cs`](KnowledgeBase.Api/KnowledgeBaseModule.cs)
- typed module defaults, validated startup binding, persisted runtime settings, and audited settings updates in [`KnowledgeBase.Application/Settings/KnowledgeBaseSettingsContracts.cs`](KnowledgeBase.Application/Settings/KnowledgeBaseSettingsContracts.cs) and [`KnowledgeBase.Infrastructure/Persistence/PostgresKnowledgeBaseStore.cs`](KnowledgeBase.Infrastructure/Persistence/PostgresKnowledgeBaseStore.cs)
- a module with PostgreSQL-backed persistence in [`KnowledgeBase.Infrastructure/KnowledgeBaseInfrastructureServiceCollectionExtensions.cs`](KnowledgeBase.Infrastructure/KnowledgeBaseInfrastructureServiceCollectionExtensions.cs)
- module-owned persistence and seed data in [`KnowledgeBase.Infrastructure/Persistence/PostgresKnowledgeBaseStore.cs`](KnowledgeBase.Infrastructure/Persistence/PostgresKnowledgeBaseStore.cs) and [`KnowledgeBase.Infrastructure/Persistence/KnowledgeBaseSeedData.cs`](KnowledgeBase.Infrastructure/Persistence/KnowledgeBaseSeedData.cs)
- a shared published-entry query seam for other modules in [`KnowledgeBase.PublicContracts/Queries/IKnowledgeBasePublishedEntryQueryService.cs`](KnowledgeBase.PublicContracts/Queries/IKnowledgeBasePublishedEntryQueryService.cs)
- idempotent publish behavior plus a real additive-first integration-contract evolution example in [`KnowledgeBase.PublicContracts/Events/KnowledgeEntryPublishedEventV1.cs`](KnowledgeBase.PublicContracts/Events/KnowledgeEntryPublishedEventV1.cs) and [`KnowledgeBase.PublicContracts/Events/KnowledgeEntryPublishedEventV2.cs`](KnowledgeBase.PublicContracts/Events/KnowledgeEntryPublishedEventV2.cs)
- module-owned outbox wiring in [`KnowledgeBase.Infrastructure/Outbox/KnowledgeBaseOutboxRegistration.cs`](KnowledgeBase.Infrastructure/Outbox/KnowledgeBaseOutboxRegistration.cs)
- a public browser route at [`../../../web/src/features/knowledge-base/index.ts`](../../../web/src/features/knowledge-base/index.ts)

## Start Here

- API edge: [`KnowledgeBase.Api/KnowledgeBaseModule.cs`](KnowledgeBase.Api/KnowledgeBaseModule.cs)
- infrastructure wiring: [`KnowledgeBase.Infrastructure/KnowledgeBaseInfrastructureServiceCollectionExtensions.cs`](KnowledgeBase.Infrastructure/KnowledgeBaseInfrastructureServiceCollectionExtensions.cs)
- runtime settings model: [`KnowledgeBase.Application/Settings/KnowledgeBaseSettingsContracts.cs`](KnowledgeBase.Application/Settings/KnowledgeBaseSettingsContracts.cs)
- shared query adapter: [`KnowledgeBase.Infrastructure/Queries/KnowledgeBasePublishedEntryQueryService.cs`](KnowledgeBase.Infrastructure/Queries/KnowledgeBasePublishedEntryQueryService.cs)
- browser feature: [`../../../web/src/features/knowledge-base/KnowledgeBaseFeature.tsx`](../../../web/src/features/knowledge-base/KnowledgeBaseFeature.tsx)
- general integration coverage: [`../../../tests/Integration.Tests/Modules/KnowledgeBase/KnowledgeBaseIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/KnowledgeBase/KnowledgeBaseIntegrationTests.cs)
- configuration governance coverage: [`../../../tests/Integration.Tests/Modules/KnowledgeBase/KnowledgeBaseConfigurationIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/KnowledgeBase/KnowledgeBaseConfigurationIntegrationTests.cs)
- outbox and idempotency coverage: [`../../../tests/Integration.Tests/Modules/KnowledgeBase/Events/KnowledgeBaseOutboxIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/KnowledgeBase/Events/KnowledgeBaseOutboxIntegrationTests.cs)

## Use This Module When

- you need a public read surface plus operator-managed writes in one module
- you want a live example of namespaced typed configuration that validates at startup and is audited when operators change it at runtime
- you want to study optimistic concurrency conflict handling for operator settings (stale-version rejection with 409 Conflict)
- you want a frontend reference for conflict resolution, session-expiry handling, save-state feedback, and dirty-state tracking
- you want a more realistic reference than `SampleFeature`
- you want to study publish-time idempotency, dual-published compatibility windows, and public-event emission on a richer domain model
