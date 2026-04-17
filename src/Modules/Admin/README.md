# Admin

This module is reference material, not scaffolding. Add new modules with [`../../../scripts/New-Module.ps1`](../../../scripts/New-Module.ps1) and [`../../../templates/module/module-spec.example.json`](../../../templates/module/module-spec.example.json).

## Purpose

`Admin` is the advanced reference module in this baseline. Use it when you need to study cross-module event consumption, replay-safe projections, machine endpoints, background recovery, public read models, bounded shared reads, and shared browser realtime.

`Admin` is the only teaching module allowed to depend backward into other teaching modules. It consumes `SampleFeature.PublicContracts` (events) and `KnowledgeBase.PublicContracts` (events plus a bounded shared-read example). The simpler teaching modules (`SampleFeature`, `Blog`, `KnowledgeBase`) intentionally know nothing about `Admin`. This keeps the teaching graph directional: removing a simpler module only requires deleting the matching consumer files inside `Admin`, never edits in the other simpler modules.

## What It Teaches

- public read and machine endpoint surfaces in [`Admin.Api/AdminModule.cs`](Admin.Api/AdminModule.cs)
- public query contracts in [`Admin.PublicContracts/Queries/AdminAnnouncementReadModel.cs`](Admin.PublicContracts/Queries/AdminAnnouncementReadModel.cs) and [`Admin.PublicContracts/Queries/AdminAnnouncementListResponse.cs`](Admin.PublicContracts/Queries/AdminAnnouncementListResponse.cs)
- a second shared-query consumer shape through [`Admin.Application/Queries/ListAdminGuidanceQuery.cs`](Admin.Application/Queries/ListAdminGuidanceQuery.cs) and the browser panel in [`../../../web/src/features/admin/AdminFeature.tsx`](../../../web/src/features/admin/AdminFeature.tsx)
- replay-safe consumer paths in [`Admin.Application/Consumers/SampleAnnouncementPublishedConsumer.cs`](Admin.Application/Consumers/SampleAnnouncementPublishedConsumer.cs)
- module-owned inbox persistence split into write side [`Admin.Infrastructure/Inbox/AdminAnnouncementInboxWriter.cs`](Admin.Infrastructure/Inbox/AdminAnnouncementInboxWriter.cs) (schema owner) and read side [`Admin.Infrastructure/Inbox/AdminAnnouncementReader.cs`](Admin.Infrastructure/Inbox/AdminAnnouncementReader.cs)
- process-manager orchestration in [`Admin.Application/ProcessManagers/AdminAnnouncementProjectionProcessManager.cs`](Admin.Application/ProcessManagers/AdminAnnouncementProjectionProcessManager.cs)
- persisted checkpoint storage in [`Admin.Infrastructure/ProcessManagers/AdminAnnouncementProjectionProcessManagerStore.cs`](Admin.Infrastructure/ProcessManagers/AdminAnnouncementProjectionProcessManagerStore.cs)
- recovery worker wiring in [`Admin.Infrastructure/Workers/AdminAnnouncementProjectionRecoveryWorker.cs`](Admin.Infrastructure/Workers/AdminAnnouncementProjectionRecoveryWorker.cs)
- shared browser realtime integration in [`Admin.Infrastructure/Realtime/AdminAnnouncementRealtimeNotifier.cs`](Admin.Infrastructure/Realtime/AdminAnnouncementRealtimeNotifier.cs) and [`../../../web/src/features/admin/adminRealtime.ts`](../../../web/src/features/admin/adminRealtime.ts)

## Start Here

- API edge: [`Admin.Api/AdminModule.cs`](Admin.Api/AdminModule.cs)
- infrastructure wiring: [`Admin.Infrastructure/AdminInfrastructureServiceCollectionExtensions.cs`](Admin.Infrastructure/AdminInfrastructureServiceCollectionExtensions.cs)
- browser feature: [`../../../web/src/features/admin/AdminFeature.tsx`](../../../web/src/features/admin/AdminFeature.tsx)
- consumer replay coverage: [`../../../tests/Integration.Tests/Modules/Admin/Events/AdminConsumerReplayIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/Admin/Events/AdminConsumerReplayIntegrationTests.cs)
- process-manager coverage: [`../../../tests/Integration.Tests/Modules/Admin/ProcessManagers/AdminAnnouncementProjectionProcessManagerIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/Admin/ProcessManagers/AdminAnnouncementProjectionProcessManagerIntegrationTests.cs)
- machine-endpoint coverage: [`../../../tests/Integration.Tests/Modules/Admin/Machine/AdminMachineAnnouncementsIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/Admin/Machine/AdminMachineAnnouncementsIntegrationTests.cs)
- realtime coverage: [`../../../tests/Integration.Tests/Modules/Admin/Realtime/AdminRealtimeIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/Admin/Realtime/AdminRealtimeIntegrationTests.cs)
- recovery-worker coverage: [`../../../tests/Integration.Tests/Modules/Admin/Workers/AdminAnnouncementProjectionRecoveryWorkerIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/Admin/Workers/AdminAnnouncementProjectionRecoveryWorkerIntegrationTests.cs)

## Use This Module When

- you need a reference for projecting another module's integration events into your own read model
- you need replay safety, persisted process-manager state, and background recovery in the same module
- you need a module that exposes both browser-facing operations and machine-consumable endpoints
- you want to study how recovery workers coordinate with module state transitions (pausing while disabled, resuming after re-enable)
- you want to verify that consumer replay survives module disable/re-enable cycles without producing duplicates