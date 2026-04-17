using NodaTime;

namespace BuildingBlocks.Infrastructure.Time;

internal sealed class SystemClockAdapter : BuildingBlocks.Domain.Time.IClock
{
    public Instant GetCurrentInstant()
    {
        return SystemClock.Instance.GetCurrentInstant();
    }
}
