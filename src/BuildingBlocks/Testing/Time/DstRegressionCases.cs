using NodaTime;
using NodaTime.TimeZones;

namespace BuildingBlocks.Testing.Time;

public sealed record DstLocalRegressionCase(string TimeZoneId, LocalDateTime LocalDateTime);

public static class DstRegressionCases
{
    public const string EasternTimeZoneId = "America/New_York";

    public static DstLocalRegressionCase SpringForwardGap { get; } =
        new(EasternTimeZoneId, new LocalDateTime(2026, 3, 8, 2, 30));

    public static DstLocalRegressionCase FallBackAmbiguous { get; } =
        new(EasternTimeZoneId, new LocalDateTime(2026, 11, 1, 1, 30));

    public static ZonedDateTime ResolveSpringForwardGapLater()
    {
        var zone = GetZone(SpringForwardGap.TimeZoneId);
        return zone.AtLeniently(SpringForwardGap.LocalDateTime);
    }

    public static ZonedDateTime ResolveFallBackEarlier()
    {
        var zone = GetZone(FallBackAmbiguous.TimeZoneId);
        return zone.ResolveLocal(
            FallBackAmbiguous.LocalDateTime,
            Resolvers.CreateMappingResolver(Resolvers.ReturnEarlier, Resolvers.ThrowWhenSkipped));
    }

    public static ZonedDateTime ResolveFallBackLater()
    {
        var zone = GetZone(FallBackAmbiguous.TimeZoneId);
        return zone.ResolveLocal(
            FallBackAmbiguous.LocalDateTime,
            Resolvers.CreateMappingResolver(Resolvers.ReturnLater, Resolvers.ThrowWhenSkipped));
    }

    private static DateTimeZone GetZone(string timeZoneId)
    {
        return DateTimeZoneProviders.Tzdb[timeZoneId];
    }
}
