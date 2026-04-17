using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Testing.Time;
using Module.UnitTests.Support;
using Module.UnitTests.SampleFeature;
using NodaTime;
using SampleFeature.Application.Publishing;
using SampleFeature.Application.Scheduling;

namespace Module.UnitTests.SampleFeature.Scheduling;

public sealed class ScheduleSampleAnnouncementCommandHandlerTests
{
    [Fact]
    public async Task Handle_returns_unauthorized_when_the_current_actor_is_missing()
    {
        var auditWriter = new RecordingAuditEventWriter();
        var timeZoneReader = new StubSampleFeatureIdentityTimeZoneReader();
        var store = new RecordingScheduledSampleAnnouncementStore();
        var handler = CreateHandler(
            auditWriter,
            new FakeClock(Instant.FromUtc(2026, 4, 16, 7, 0)),
            new StubCurrentActorAccessor(CurrentActor.Anonymous),
            new InMemoryRequestContextAccessor(),
            timeZoneReader,
            store);

        var result = await handler.Handle(
            new ScheduleSampleAnnouncementCommand(
                "request-1",
                "Scheduled announcement",
                "Body",
                new LocalDate(2026, 4, 20),
                new LocalTime(9, 0)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleAnnouncementSchedulingErrors.CurrentActorRequired().Code, result.Error.Code);
        Assert.Empty(timeZoneReader.ActorIds);
        Assert.Empty(store.ScheduledAnnouncements);
        Assert.Empty(auditWriter.Events);
    }

    [Fact]
    public async Task Handle_schedules_the_announcement_and_writes_the_audit_event()
    {
        var createdUtc = Instant.FromUtc(2026, 4, 16, 7, 30);
        var auditWriter = new RecordingAuditEventWriter();
        var timeZoneReader = new StubSampleFeatureIdentityTimeZoneReader
        {
            NextResult = BuildingBlocks.Application.Results.Result<string>.Success("Etc/UTC")
        };
        var store = new RecordingScheduledSampleAnnouncementStore();
        var requestContextAccessor = new InMemoryRequestContextAccessor
        {
            Current = new RequestContext("corr-2", "req-2")
        };
        var handler = CreateHandler(
            auditWriter,
            new FakeClock(createdUtc),
            new StubCurrentActorAccessor(new CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            requestContextAccessor,
            timeZoneReader,
            store);

        var result = await handler.Handle(
            new ScheduleSampleAnnouncementCommand(
                "request-1",
                "Scheduled announcement",
                "Body",
                new LocalDate(2026, 4, 20),
                new LocalTime(9, 0)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["admin-1"], timeZoneReader.ActorIds);

        var scheduled = Assert.Single(store.ScheduledAnnouncements);
        Assert.Equal("Scheduled announcement", scheduled.Title);
        Assert.Equal("Body", scheduled.Body);
        Assert.Equal("Etc/UTC", scheduled.TimeZoneId);
        Assert.Equal("admin-1", scheduled.ScheduledByActorId);
        Assert.Equal(SampleAnnouncementSchedulingStatuses.Pending, scheduled.Status);
        Assert.Equal(SampleAnnouncementLocalTimeResolutions.Exact, scheduled.LocalTimeResolution);
        Assert.Equal(createdUtc, scheduled.CreatedUtc);
        Assert.Equal(createdUtc, scheduled.UpdatedUtc);
        Assert.Equal(scheduled, result.Value);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal(SampleAnnouncementAuditing.ActionSchedule, audit.Action);
        Assert.Equal(SampleAnnouncementAuditing.TargetTypeScheduledAnnouncement, audit.TargetType);
        Assert.Equal(scheduled.ScheduledAnnouncementId.ToString(), audit.TargetId);
        Assert.Equal(SampleAnnouncementAuditing.OutcomeScheduled, audit.Outcome);
        Assert.Equal("admin-1", audit.ActorId);
        Assert.Equal("corr-2", audit.CorrelationId);
        Assert.Equal(createdUtc, audit.OccurredUtc);
    }

    private static ScheduleSampleAnnouncementCommandHandler CreateHandler(
        RecordingAuditEventWriter auditWriter,
        FakeClock clock,
        StubCurrentActorAccessor actorAccessor,
        InMemoryRequestContextAccessor requestContextAccessor,
        StubSampleFeatureIdentityTimeZoneReader timeZoneReader,
        RecordingScheduledSampleAnnouncementStore store)
    {
        return new ScheduleSampleAnnouncementCommandHandler(
            auditWriter,
            clock,
            actorAccessor,
            requestContextAccessor,
            timeZoneReader,
            store);
    }
}
