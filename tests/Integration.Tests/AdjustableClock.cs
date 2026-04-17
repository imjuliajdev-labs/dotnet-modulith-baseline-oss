using NodaTime;
using IClock = BuildingBlocks.Domain.Time.IClock;

namespace Integration.Tests;

internal sealed class AdjustableClock : IClock
{
    private Instant _currentInstant;

    public AdjustableClock(Instant currentInstant)
    {
        _currentInstant = currentInstant;
    }

    public Instant GetCurrentInstant()
    {
        return _currentInstant;
    }

    public void Advance(Duration duration)
    {
        _currentInstant += duration;
    }
}
