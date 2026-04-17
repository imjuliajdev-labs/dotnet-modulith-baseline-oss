using BuildingBlocks.Domain.Contracts;
using BuildingBlocks.Domain.Events;
using NodaTime;

namespace Blog.PublicContracts.Events;

// Teaching reference: published by Blog but intentionally unconsumed. Downstream modules adopt this contract when they need to react to blog publications.
[ContractLifecycle("2025-06-01", ContractLifecycleStatus.Active)]
public sealed record BlogPostPublishedEventV1(
    Guid EventId,
    Instant OccurredAt,
    Guid PostId,
    string Slug,
    string Title,
    string Summary,
    string Body,
    string PublishedByActorId)
    : IIntegrationEvent;
