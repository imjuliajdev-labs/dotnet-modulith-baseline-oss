using BuildingBlocks.Domain.Contracts;
using BuildingBlocks.Domain.Events;
using NodaTime;

namespace SampleFeature.PublicContracts.Events;

[ContractLifecycle("2025-06-01", ContractLifecycleStatus.Active)]
public sealed record SampleAnnouncementPublishedEventV1(
    Guid EventId,
    Instant OccurredAt,
    Guid AnnouncementId,
    string Title,
    string Body,
    string PublishedByActorId) : IIntegrationEvent;
