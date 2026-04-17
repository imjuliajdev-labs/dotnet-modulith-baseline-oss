# Blog

This module is reference material, not scaffolding. Add new modules with [`../../../scripts/New-Module.ps1`](../../../scripts/New-Module.ps1) and [`../../../templates/module/module-spec.example.json`](../../../templates/module/module-spec.example.json).

## Purpose

`Blog` is the first production-grade EF-first teaching module in the baseline. Use it when you want a bounded example of a real aggregate, EF mapping, migrations, public reads, editorial operations, time-zone-aware scheduling, outbox publication, and a matching frontend surface without jumping straight to the larger `KnowledgeBase` content model.

## What It Teaches

- a bounded public-read plus operator editorial API surface in [`Blog.Api/BlogModule.cs`](Blog.Api/BlogModule.cs)
- a real EF Core aggregate, persistence store, and migration-ready DbContext in [`Blog.Infrastructure/Persistence/BlogPersistenceDbContext.cs`](Blog.Infrastructure/Persistence/BlogPersistenceDbContext.cs), [`Blog.Infrastructure/Persistence/BlogPostStore.cs`](Blog.Infrastructure/Persistence/BlogPostStore.cs), and [`Blog.Infrastructure/Persistence/Migrations`](Blog.Infrastructure/Persistence/Migrations)
- post, taxonomy, and lifecycle workflows in [`Blog.Application/Posts/BlogPostContracts.cs`](Blog.Application/Posts/BlogPostContracts.cs), [`Blog.Application/Taxonomy/BlogTaxonomyContracts.cs`](Blog.Application/Taxonomy/BlogTaxonomyContracts.cs), and [`Blog.Application/Scheduling/BlogPostSchedulingContracts.cs`](Blog.Application/Scheduling/BlogPostSchedulingContracts.cs)
- time-zone-aware due-work processing in [`Blog.Infrastructure/Workers/BlogPublicationWorker.cs`](Blog.Infrastructure/Workers/BlogPublicationWorker.cs)
- module-owned integration-event publication in [`Blog.Infrastructure/Outbox/BlogOutboxRegistration.cs`](Blog.Infrastructure/Outbox/BlogOutboxRegistration.cs) and [`Blog.PublicContracts/Events/BlogPostPublishedEventV1.cs`](Blog.PublicContracts/Events/BlogPostPublishedEventV1.cs)
- a synchronous public read contract for other modules in [`Blog.PublicContracts/Queries/IBlogPublishedPostQueryService.cs`](Blog.PublicContracts/Queries/IBlogPublishedPostQueryService.cs)
- typed operator-managed setting metadata in [`Blog.Infrastructure/Configuration/BlogInfrastructureOptions.cs`](Blog.Infrastructure/Configuration/BlogInfrastructureOptions.cs) and [`Blog.Application/Settings/BlogSettingsContracts.cs`](Blog.Application/Settings/BlogSettingsContracts.cs)
- a matching public plus operator frontend in [`../../../web/src/features/blog/BlogFeature.tsx`](../../../web/src/features/blog/BlogFeature.tsx)

## Start Here

- API edge: [`Blog.Api/BlogModule.cs`](Blog.Api/BlogModule.cs)
- post contracts and normalization: [`Blog.Application/Posts/BlogPostContracts.cs`](Blog.Application/Posts/BlogPostContracts.cs)
- taxonomy contracts: [`Blog.Application/Taxonomy/BlogTaxonomyContracts.cs`](Blog.Application/Taxonomy/BlogTaxonomyContracts.cs)
- scheduling contracts: [`Blog.Application/Scheduling/BlogPostSchedulingContracts.cs`](Blog.Application/Scheduling/BlogPostSchedulingContracts.cs)
- persistence DbContext: [`Blog.Infrastructure/Persistence/BlogPersistenceDbContext.cs`](Blog.Infrastructure/Persistence/BlogPersistenceDbContext.cs)
- store implementation: [`Blog.Infrastructure/Persistence/BlogPostStore.cs`](Blog.Infrastructure/Persistence/BlogPostStore.cs)
- outbox registration: [`Blog.Infrastructure/Outbox/BlogOutboxRegistration.cs`](Blog.Infrastructure/Outbox/BlogOutboxRegistration.cs)
- due-work worker: [`Blog.Infrastructure/Workers/BlogPublicationWorker.cs`](Blog.Infrastructure/Workers/BlogPublicationWorker.cs)
- query service adapter: [`Blog.Infrastructure/Queries/BlogPublishedPostQueryService.cs`](Blog.Infrastructure/Queries/BlogPublishedPostQueryService.cs)
- frontend feature: [`../../../web/src/features/blog/BlogFeature.tsx`](../../../web/src/features/blog/BlogFeature.tsx)
- integration coverage: [`../../../tests/Integration.Tests/Modules/Blog/Posts/BlogPostsIntegrationTests.cs`](../../../tests/Integration.Tests/Modules/Blog/Posts/BlogPostsIntegrationTests.cs)

## Use This Module When

- you want the first EF-first reference that includes a real aggregate plus a real migration
- you want a public-read and operator editorial workflow with taxonomy and lifecycle scheduling
- you want a module-owned outbox and due-work worker on top of an EF-backed module
- you want a matching frontend reference for public plus operator UX
- you want a module-owned synchronous shared-read seam exposed through `.PublicContracts`
- you want a bounded example of typed operator-managed setting definitions in the scaffolded configuration shape
