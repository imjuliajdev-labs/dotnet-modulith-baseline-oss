using NodaTime;

namespace BuildingBlocks.Domain.Events;

public interface IIntegrationEvent
{
    Guid EventId { get; }

    Instant OccurredAt { get; }
}
