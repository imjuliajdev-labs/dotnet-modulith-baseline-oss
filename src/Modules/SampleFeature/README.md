# SampleFeature

This module is reference material, not scaffolding. Add new modules with [`../../../scripts/New-Module.ps1`](../../../scripts/New-Module.ps1) and [`../../../templates/module/module-spec.example.json`](../../../templates/module/module-spec.example.json).

## Purpose

`SampleFeature` is the smallest end-to-end reference module in the repo. Use it when you want the simplest concrete example of a governed module after scaffolding.

`SampleFeature` is also the cleanest illustration of a fully self-contained teaching module. It does not depend on any other teaching module's `PublicContracts`. It can be removed without breaking `Blog`, `KnowledgeBase`, or any other simpler reference. Only `Admin` consumes its public integration event, and removing `SampleFeature` requires deleting that single consumer in `Admin`.

## What It Teaches

- a minimal `IApiModule` descriptor and endpoint surface in [`SampleFeature.Api/SampleFeatureModule.cs`](SampleFeature.Api/SampleFeatureModule.cs)
- a small but real domain value object for announcement content normalization in [`SampleFeature.Domain/Announcements/SampleAnnouncementContent.cs`](SampleFeature.Domain/Announcements/SampleAnnouncementContent.cs)
- an antiforgery-protected write endpoint with an `Idempotency-Key` requirement
- EF Core persistence with a module-owned DbContext and migrations in [`SampleFeature.Infrastructure/Persistence/SampleFeaturePersistenceDbContext.cs`](SampleFeature.Infrastructure/Persistence/SampleFeaturePersistenceDbContext.cs)
- module-owned outbox registration in [`SampleFeature.Infrastructure/Outbox/SampleFeatureOutboxRegistration.cs`](SampleFeature.Infrastructure/Outbox/SampleFeatureOutboxRegistration.cs)
- a public integration event contract in [`SampleFeature.PublicContracts/Events/SampleAnnouncementPublishedEventV1.cs`](SampleFeature.PublicContracts/Events/SampleAnnouncementPublishedEventV1.cs)
- DST-aware scheduling that resolves browser-supplied local times through the current actor's preferred IANA zone in [`SampleFeature.Application/Scheduling`](SampleFeature.Application/Scheduling)
- a lazy frontend feature manifest in [`../../../web/src/features/sampleFeature/index.ts`](../../../web/src/features/sampleFeature/index.ts)

For an example of how a more advanced module reaches back into a simpler module's public surface (the only allowed backward direction in the teaching set), see [`Admin`](../Admin/README.md) and its `Admin.Application/SharedReads/AdminKnowledgeBaseGuidanceReader.cs`.

## Start Here

- API edge: [`SampleFeature.Api/SampleFeatureModule.cs`](SampleFeature.Api/SampleFeatureModule.cs)
- domain content normalization: [`SampleFeature.Domain/Announcements/SampleAnnouncementContent.cs`](SampleFeature.Domain/Announcements/SampleAnnouncementContent.cs)
- persistence DbContext: [`SampleFeature.Infrastructure/Persistence/SampleFeaturePersistenceDbContext.cs`](SampleFeature.Infrastructure/Persistence/SampleFeaturePersistenceDbContext.cs)
- infrastructure wiring: [`SampleFeature.Infrastructure/SampleFeatureInfrastructureServiceCollectionExtensions.cs`](SampleFeature.Infrastructure/SampleFeatureInfrastructureServiceCollectionExtensions.cs)
- outbox coverage: [`../../../tests/Integration.Tests/Modules/SampleFeature/Events/SampleFeatureOutboxIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/SampleFeature/Events/SampleFeatureOutboxIntegrationTests.cs)
- DST coverage: [`../../../tests/Integration.Tests/Modules/SampleFeature/Time/SampleFeatureScheduledAnnouncementDstIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/SampleFeature/Time/SampleFeatureScheduledAnnouncementDstIntegrationTests.cs)
- browser route: [`../../../web/src/features/sampleFeature/SampleFeature.tsx`](../../../web/src/features/sampleFeature/SampleFeature.tsx)

## Use This Module When

- you want the smallest event-producing module reference
- you want to copy the baseline's idempotent write plus outbox pattern
- you want a fully self-contained reference that demonstrates the governed shape without cross-module reads
- you want to see the minimum optional frontend feature discovered through the Platform manifest
