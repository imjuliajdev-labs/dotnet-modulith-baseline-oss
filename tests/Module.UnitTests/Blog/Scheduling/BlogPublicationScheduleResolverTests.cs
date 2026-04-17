using Blog.Application.Scheduling;
using BuildingBlocks.Testing.Time;
using NodaTime;

namespace Module.UnitTests.ModuleCoverage.Blog.Scheduling;

public sealed class BlogPublicationScheduleResolverTests
{
    [Fact]
    public void ExactLocalTimeMapsWithoutAdjustment()
    {
        var result = BlogPublicationScheduleResolver.Resolve(
            new LocalDate(2026, 4, 6),
            new LocalTime(9, 15),
            DstRegressionCases.EasternTimeZoneId,
            "identity:seeded-admin");

        Xunit.Assert.Equal(BlogPostLocalTimeResolutions.Exact, result.LocalTimeResolution);
        Xunit.Assert.Equal(Instant.FromUtc(2026, 4, 6, 13, 15), result.ScheduledForUtc);
    }

    [Fact]
    public void SpringForwardGapResolvesToTheNextValidInstant()
    {
        var result = BlogPublicationScheduleResolver.Resolve(
            DstRegressionCases.SpringForwardGap.LocalDateTime.Date,
            DstRegressionCases.SpringForwardGap.LocalDateTime.TimeOfDay,
            DstRegressionCases.SpringForwardGap.TimeZoneId,
            "identity:seeded-admin");

        Xunit.Assert.Equal(BlogPostLocalTimeResolutions.SkippedForward, result.LocalTimeResolution);
        Xunit.Assert.Equal(DstRegressionCases.ResolveSpringForwardGapLater().ToInstant(), result.ScheduledForUtc);
    }

    [Fact]
    public void FallBackAmbiguousLocalTimeChoosesTheEarlierOccurrence()
    {
        var result = BlogPublicationScheduleResolver.Resolve(
            DstRegressionCases.FallBackAmbiguous.LocalDateTime.Date,
            DstRegressionCases.FallBackAmbiguous.LocalDateTime.TimeOfDay,
            DstRegressionCases.FallBackAmbiguous.TimeZoneId,
            "identity:seeded-admin");

        Xunit.Assert.Equal(BlogPostLocalTimeResolutions.AmbiguousEarlier, result.LocalTimeResolution);
        Xunit.Assert.Equal(DstRegressionCases.ResolveFallBackEarlier().ToInstant(), result.ScheduledForUtc);
    }
}
