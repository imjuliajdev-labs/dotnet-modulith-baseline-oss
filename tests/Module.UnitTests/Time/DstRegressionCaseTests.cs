using BuildingBlocks.Testing.Time;
using NodaTime;

namespace Module.UnitTests.Time;

public sealed class DstRegressionCaseTests
{
    [Xunit.Fact]
    public void SpringForwardGapFixtureRepresentsASkippedLocalTimeInAnIanaZone()
    {
        var zone = DateTimeZoneProviders.Tzdb[DstRegressionCases.SpringForwardGap.TimeZoneId];
        var mapping = zone.MapLocal(DstRegressionCases.SpringForwardGap.LocalDateTime);

        Xunit.Assert.Equal(DstRegressionCases.EasternTimeZoneId, zone.Id);
        Xunit.Assert.Equal(0, mapping.Count);

        var resolved = DstRegressionCases.ResolveSpringForwardGapLater();

        Xunit.Assert.Equal(new LocalDateTime(2026, 3, 8, 3, 30), resolved.LocalDateTime);
        Xunit.Assert.Equal(Offset.FromHours(-4), resolved.Offset);
    }

    [Xunit.Fact]
    public void FallBackAmbiguousFixtureExposesBothOccurrencesOfTheSameLocalTime()
    {
        var zone = DateTimeZoneProviders.Tzdb[DstRegressionCases.FallBackAmbiguous.TimeZoneId];
        var mapping = zone.MapLocal(DstRegressionCases.FallBackAmbiguous.LocalDateTime);

        Xunit.Assert.Equal(2, mapping.Count);

        var earlier = DstRegressionCases.ResolveFallBackEarlier();
        var later = DstRegressionCases.ResolveFallBackLater();

        Xunit.Assert.Equal(DstRegressionCases.FallBackAmbiguous.LocalDateTime, earlier.LocalDateTime);
        Xunit.Assert.Equal(DstRegressionCases.FallBackAmbiguous.LocalDateTime, later.LocalDateTime);
        Xunit.Assert.NotEqual(earlier.Offset, later.Offset);
        Xunit.Assert.NotEqual(earlier.ToInstant(), later.ToInstant());
    }
}
