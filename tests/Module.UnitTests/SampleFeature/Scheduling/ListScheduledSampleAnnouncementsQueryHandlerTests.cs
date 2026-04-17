using BuildingBlocks.Application.Actors;
using Module.UnitTests.Support;
using Module.UnitTests.SampleFeature;
using NodaTime;
using SampleFeature.Application.Scheduling;

namespace Module.UnitTests.SampleFeature.Scheduling;

public sealed class ListScheduledSampleAnnouncementsQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_unauthorized_when_the_current_actor_is_missing()
    {
        var store = new RecordingScheduledSampleAnnouncementStore();
        var handler = new ListScheduledSampleAnnouncementsQueryHandler(
            new StubCurrentActorAccessor(CurrentActor.Anonymous),
            store);

        var result = await handler.Handle(new ListScheduledSampleAnnouncementsQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleAnnouncementSchedulingErrors.CurrentActorRequired().Code, result.Error.Code);
        Assert.Null(store.LastListActorId);
        Assert.Null(store.LastListLimit);
    }

    [Fact]
    public async Task Handle_clamps_the_requested_limit_before_reading_from_the_store()
    {
        var scheduled = new ScheduledSampleAnnouncement(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Scheduled announcement",
            "Body",
            new LocalDate(2026, 4, 20),
            new LocalTime(9, 0),
            "Etc/UTC",
            Instant.FromUtc(2026, 4, 20, 9, 0),
            "admin-1",
            SampleAnnouncementLocalTimeResolutions.Exact,
            SampleAnnouncementSchedulingStatuses.Pending,
            Instant.FromUtc(2026, 4, 16, 7, 0),
            Instant.FromUtc(2026, 4, 16, 7, 0));

        var store = new RecordingScheduledSampleAnnouncementStore
        {
            ListResult = [scheduled]
        };
        var handler = new ListScheduledSampleAnnouncementsQueryHandler(
            new StubCurrentActorAccessor(new CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            store);

        var result = await handler.Handle(new ListScheduledSampleAnnouncementsQuery(999), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("admin-1", store.LastListActorId);
        Assert.Equal(SampleAnnouncementSchedulingDefaults.MaxListLimit, store.LastListLimit);
        var item = Assert.Single(result.Value.Announcements);
        Assert.Equal(scheduled, item);
    }
}
