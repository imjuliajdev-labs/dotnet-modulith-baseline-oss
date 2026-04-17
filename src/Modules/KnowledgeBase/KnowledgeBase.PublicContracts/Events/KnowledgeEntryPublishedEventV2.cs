using BuildingBlocks.Domain.Contracts;
using BuildingBlocks.Domain.Events;
using NodaTime;

namespace KnowledgeBase.PublicContracts.Events;

[ContractLifecycle("2026-04-10", ContractLifecycleStatus.Active)]
public sealed record KnowledgeEntryPublishedEventV2(
    Guid EventId,
    Instant OccurredAt,
    Guid EntryId,
    string Slug,
    string Title,
    string Body,
    string Category,
    string PublishedByActorId) : IIntegrationEvent;
