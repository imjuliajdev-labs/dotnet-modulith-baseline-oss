using BuildingBlocks.Domain.Contracts;
using BuildingBlocks.Domain.Events;
using NodaTime;

namespace KnowledgeBase.PublicContracts.Events;

[ContractLifecycle(
    "2025-06-01",
    ContractLifecycleStatus.Superseded,
    SupersededBy = typeof(KnowledgeEntryPublishedEventV2),
    RetireOn = "2027-01-15")]
public sealed record KnowledgeEntryPublishedEventV1(
    Guid EventId,
    Instant OccurredAt,
    Guid EntryId,
    string Slug,
    string Title,
    string Body,
    string PublishedByActorId) : IIntegrationEvent;
