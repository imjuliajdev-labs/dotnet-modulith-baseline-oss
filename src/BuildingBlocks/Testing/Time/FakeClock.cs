using NodaTime;

namespace BuildingBlocks.Testing.Time;

public sealed class FakeClock : BuildingBlocks.Domain.Time.IClock
{
    private Instant _currentInstant;

    public FakeClock(Instant currentInstant)
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

    public void Set(Instant instant)
    {
        _currentInstant = instant;
    }
}
