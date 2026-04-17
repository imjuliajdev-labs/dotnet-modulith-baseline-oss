using NodaTime;

namespace BuildingBlocks.Domain.Time;

public interface IClock
{
    Instant GetCurrentInstant();
}
