using BuildingBlocks.Testing.Time;
using Module.UnitTests.SampleFeature;
using NodaTime;
using SampleFeature.Application.Publishing;
using SampleFeature.Application.Scheduling;

namespace Module.UnitTests.SampleFeature.Scheduling;

public sealed class ProcessDueScheduledSampleAnnouncementsCommandHandlerTests
{
    [Fact]
    public async Task Handle_publishes_each_leased_announcement_marks_it_published_and_returns_the_count()
    {
        var now = Instant.FromUtc(2026, 4, 16, 8, 0);
        var firstLeaseId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var secondLeaseId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var store = new RecordingScheduledSampleAnnouncementStore
        {
            LeaseResult =
            [
                new LeasedScheduledSampleAnnouncement(
                    Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    "First",
                    "First body",
                    new LocalDate(2026, 4, 16),
                    new LocalTime(8, 0),
                    "Etc/UTC",
                    Instant.FromUtc(2026, 4, 16, 8, 0),
                    "admin-1",
                    SampleAnnouncementLocalTimeResolutions.Exact,
                    firstLeaseId,
                    now,
                    now),
                new LeasedScheduledSampleAnnouncement(
                    Guid.Parse("66666666-6666-6666-6666-666666666666"),
                    "Second",
                    "Second body",
                    new LocalDate(2026, 4, 16),
                    new LocalTime(8, 30),
                    "Etc/UTC",
                    Instant.FromUtc(2026, 4, 16, 8, 30),
                    "admin-2",
                    SampleAnnouncementLocalTimeResolutions.Exact,
                    secondLeaseId,
                    now,
                    now)
            ]
        };

        var publisher = new RecordingSampleAnnouncementPublisher();
        publisher.PublishedAnnouncements.Enqueue(
            new PublishedSampleAnnouncement(
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                "First",
                "First body",
                Instant.FromUtc(2026, 4, 16, 8, 0),
                "admin-1"));
        publisher.PublishedAnnouncements.Enqueue(
            new PublishedSampleAnnouncement(
                Guid.Parse("88888888-8888-8888-8888-888888888888"),
                "Second",
                "Second body",
                Instant.FromUtc(2026, 4, 16, 8, 30),
                "admin-2"));

        var handler = new ProcessDueScheduledSampleAnnouncementsCommandHandler(
            new FakeClock(now),
            publisher,
            store);

        var result = await handler.Handle(new ProcessDueScheduledSampleAnnouncementsCommand(999), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        Assert.Equal(now, store.LastLeaseNow);
        Assert.Equal(SampleAnnouncementSchedulingDefaults.MaxListLimit, store.LastLeaseBatchSize);
        Assert.NotEqual(Guid.Empty, store.LastLeaseId);
        Assert.Equal(now + SampleAnnouncementSchedulingDefaults.ProcessingLeaseDuration, store.LastLeaseUntil);

        Assert.Collection(
            publisher.Calls,
            first =>
            {
                Assert.Equal("First", first.Content.Title);
                Assert.Equal("First body", first.Content.Body);
                Assert.Equal(Instant.FromUtc(2026, 4, 16, 8, 0), first.PublishedAt);
                Assert.Equal("admin-1", first.PublishedByActorId);
            },
            second =>
            {
                Assert.Equal("Second", second.Content.Title);
                Assert.Equal("Second body", second.Content.Body);
                Assert.Equal(Instant.FromUtc(2026, 4, 16, 8, 30), second.PublishedAt);
                Assert.Equal("admin-2", second.PublishedByActorId);
            });

        Assert.Collection(
            store.MarkPublishedCalls,
            first =>
            {
                Assert.Equal(Guid.Parse("55555555-5555-5555-5555-555555555555"), first.ScheduledAnnouncementId);
                Assert.Equal(firstLeaseId, first.LeaseId);
                Assert.Equal(Guid.Parse("77777777-7777-7777-7777-777777777777"), first.PublishedAnnouncementId);
                Assert.Equal(Instant.FromUtc(2026, 4, 16, 8, 0), first.PublishedUtc);
            },
            second =>
            {
                Assert.Equal(Guid.Parse("66666666-6666-6666-6666-666666666666"), second.ScheduledAnnouncementId);
                Assert.Equal(secondLeaseId, second.LeaseId);
                Assert.Equal(Guid.Parse("88888888-8888-8888-8888-888888888888"), second.PublishedAnnouncementId);
                Assert.Equal(Instant.FromUtc(2026, 4, 16, 8, 30), second.PublishedUtc);
            });
    }
}
