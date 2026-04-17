using BuildingBlocks.Testing.Time;
using NodaTime;
using SampleFeature.Application.Scheduling;

namespace Module.UnitTests.SampleFeature.Scheduling;

public sealed class SampleAnnouncementScheduleResolverTests
{
    [Fact]
    public void ExactLocalTimeMapsWithoutAdjustment()
    {
        var result = SampleAnnouncementScheduleResolver.ResolveScheduledInstant(
            new LocalDate(2026, 4, 6),
            new LocalTime(9, 15),
            DstRegressionCases.EasternTimeZoneId);

        Xunit.Assert.Equal(SampleAnnouncementLocalTimeResolutions.Exact, result.LocalTimeResolution);
        Xunit.Assert.Equal(Instant.FromUtc(2026, 4, 6, 13, 15), result.ScheduledForUtc);
    }

    [Fact]
    public void SpringForwardGapResolvesToTheNextValidInstant()
    {
        var result = SampleAnnouncementScheduleResolver.ResolveScheduledInstant(
            DstRegressionCases.SpringForwardGap.LocalDateTime.Date,
            DstRegressionCases.SpringForwardGap.LocalDateTime.TimeOfDay,
            DstRegressionCases.SpringForwardGap.TimeZoneId);

        Xunit.Assert.Equal(SampleAnnouncementLocalTimeResolutions.SkippedForward, result.LocalTimeResolution);
        Xunit.Assert.Equal(DstRegressionCases.ResolveSpringForwardGapLater().ToInstant(), result.ScheduledForUtc);
    }

    [Fact]
    public void FallBackAmbiguousLocalTimeChoosesTheEarlierOccurrence()
    {
        var result = SampleAnnouncementScheduleResolver.ResolveScheduledInstant(
            DstRegressionCases.FallBackAmbiguous.LocalDateTime.Date,
            DstRegressionCases.FallBackAmbiguous.LocalDateTime.TimeOfDay,
            DstRegressionCases.FallBackAmbiguous.TimeZoneId);

        Xunit.Assert.Equal(SampleAnnouncementLocalTimeResolutions.AmbiguousEarlier, result.LocalTimeResolution);
        Xunit.Assert.Equal(DstRegressionCases.ResolveFallBackEarlier().ToInstant(), result.ScheduledForUtc);
    }
}
