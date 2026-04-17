using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Testing.Time;
using Module.UnitTests.Support;
using NodaTime;
using SampleFeature.Application.Publishing;

namespace Module.UnitTests.SampleFeature.Publishing;

public sealed class PublishSampleAnnouncementCommandHandlerTests
{
    [Fact]
    public async Task Handle_returns_validation_failure_without_publishing_or_auditing()
    {
        var publisher = new RecordingSampleAnnouncementPublisher();
        var auditWriter = new RecordingAuditEventWriter();
        var handler = CreateHandler(
            auditWriter,
            new StubCurrentActorAccessor(new CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            new FakeClock(Instant.FromUtc(2026, 4, 16, 6, 0)),
            publisher,
            new InMemoryRequestContextAccessor());

        var result = await handler.Handle(new PublishSampleAnnouncementCommand("request-1", "", "Body"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("sample-feature.announcement_title_required", result.Error.Code);
        Assert.Empty(publisher.Calls);
        Assert.Empty(auditWriter.Events);
    }

    [Fact]
    public async Task Handle_publishes_the_announcement_and_writes_the_audit_event()
    {
        var publishedAt = Instant.FromUtc(2026, 4, 16, 6, 15);
        var publishedAnnouncement = new PublishedSampleAnnouncement(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Governed baseline update",
            "Drift-resistant release notes.",
            publishedAt,
            "admin-1");

        var publisher = new RecordingSampleAnnouncementPublisher();
        publisher.PublishedAnnouncements.Enqueue(publishedAnnouncement);

        var auditWriter = new RecordingAuditEventWriter();
        var requestContextAccessor = new InMemoryRequestContextAccessor
        {
            Current = new RequestContext("corr-1", "req-1")
        };

        var handler = CreateHandler(
            auditWriter,
            new StubCurrentActorAccessor(new CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            new FakeClock(publishedAt),
            publisher,
            requestContextAccessor);

        var result = await handler.Handle(
            new PublishSampleAnnouncementCommand("request-1", "Governed baseline update", "Drift-resistant release notes."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(publishedAnnouncement, result.Value);

        var publishCall = Assert.Single(publisher.Calls);
        Assert.Equal("Governed baseline update", publishCall.Content.Title);
        Assert.Equal("Drift-resistant release notes.", publishCall.Content.Body);
        Assert.Equal(publishedAt, publishCall.PublishedAt);
        Assert.Equal("admin-1", publishCall.PublishedByActorId);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("sample-feature", audit.ModuleKey);
        Assert.Equal(SampleAnnouncementAuditing.ActionPublish, audit.Action);
        Assert.Equal(SampleAnnouncementAuditing.TargetTypeAnnouncement, audit.TargetType);
        Assert.Equal(publishedAnnouncement.AnnouncementId.ToString(), audit.TargetId);
        Assert.Equal(SampleAnnouncementAuditing.OutcomePublished, audit.Outcome);
        Assert.Equal("admin-1", audit.ActorId);
        Assert.Equal("corr-1", audit.CorrelationId);
        Assert.Equal(publishedAt, audit.OccurredUtc);
    }

    private static PublishSampleAnnouncementCommandHandler CreateHandler(
        RecordingAuditEventWriter auditWriter,
        StubCurrentActorAccessor actorAccessor,
        FakeClock clock,
        RecordingSampleAnnouncementPublisher publisher,
        InMemoryRequestContextAccessor requestContextAccessor)
    {
        return new PublishSampleAnnouncementCommandHandler(
            auditWriter,
            actorAccessor,
            clock,
            publisher,
            requestContextAccessor);
    }
}
