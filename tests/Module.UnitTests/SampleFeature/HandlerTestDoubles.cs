using BuildingBlocks.Application.Results;
using NodaTime;
using SampleFeature.Application.Publishing;
using SampleFeature.Application.SharedReads;
using SampleFeature.Application.Scheduling;
using SampleFeature.Domain.Announcements;

namespace Module.UnitTests.SampleFeature;

internal sealed class StubSampleFeatureIdentityTimeZoneReader : ISampleFeatureIdentityTimeZoneReader
{
    public List<string> ActorIds { get; } = [];

    public Result<string> NextResult { get; set; } = Result<string>.Success("Etc/UTC");

    public ValueTask<Result<string>> GetRequiredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken)
    {
        ActorIds.Add(actorId);
        return ValueTask.FromResult(NextResult);
    }
}

internal sealed class RecordingSampleAnnouncementPublisher : ISampleAnnouncementPublisher
{
    public List<PublishCall> Calls { get; } = [];

    public Queue<PublishedSampleAnnouncement> PublishedAnnouncements { get; } = new();

    public ValueTask<PublishedSampleAnnouncement> PublishAsync(
        SampleAnnouncementContent content,
        Instant publishedAt,
        string publishedByActorId,
        CancellationToken cancellationToken)
    {
        Calls.Add(new PublishCall(content, publishedAt, publishedByActorId));

        if (PublishedAnnouncements.Count > 0)
        {
            return ValueTask.FromResult(PublishedAnnouncements.Dequeue());
        }

        return ValueTask.FromResult(
            new PublishedSampleAnnouncement(
                Guid.NewGuid(),
                content.Title,
                content.Body,
                publishedAt,
                publishedByActorId));
    }

    internal sealed record PublishCall(
        SampleAnnouncementContent Content,
        Instant PublishedAt,
        string PublishedByActorId);
}

internal sealed class RecordingScheduledSampleAnnouncementStore : IScheduledSampleAnnouncementStore
{
    public List<ScheduledSampleAnnouncement> ScheduledAnnouncements { get; } = [];

    public IReadOnlyCollection<ScheduledSampleAnnouncement> ListResult { get; set; } = [];

    public IReadOnlyCollection<LeasedScheduledSampleAnnouncement> LeaseResult { get; set; } = [];

    public string? LastListActorId { get; private set; }

    public int? LastListLimit { get; private set; }

    public Instant? LastLeaseNow { get; private set; }

    public Guid? LastLeaseId { get; private set; }

    public Instant? LastLeaseUntil { get; private set; }

    public int? LastLeaseBatchSize { get; private set; }

    public List<MarkPublishedCall> MarkPublishedCalls { get; } = [];

    public ValueTask ScheduleAsync(ScheduledSampleAnnouncement announcement, CancellationToken cancellationToken)
    {
        ScheduledAnnouncements.Add(announcement);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<ScheduledSampleAnnouncement>> ListForActorAsync(string actorId, int limit, CancellationToken cancellationToken)
    {
        LastListActorId = actorId;
        LastListLimit = limit;
        return ValueTask.FromResult(ListResult);
    }

    public ValueTask<IReadOnlyCollection<LeasedScheduledSampleAnnouncement>> LeaseDueAsync(
        Instant now,
        Guid leaseId,
        Instant leaseUntil,
        int batchSize,
        CancellationToken cancellationToken)
    {
        LastLeaseNow = now;
        LastLeaseId = leaseId;
        LastLeaseUntil = leaseUntil;
        LastLeaseBatchSize = batchSize;
        return ValueTask.FromResult(LeaseResult);
    }

    public ValueTask MarkPublishedAsync(
        Guid scheduledAnnouncementId,
        Guid leaseId,
        Guid publishedAnnouncementId,
        Instant publishedUtc,
        CancellationToken cancellationToken)
    {
        MarkPublishedCalls.Add(new MarkPublishedCall(scheduledAnnouncementId, leaseId, publishedAnnouncementId, publishedUtc));
        return ValueTask.CompletedTask;
    }

    internal sealed record MarkPublishedCall(
        Guid ScheduledAnnouncementId,
        Guid LeaseId,
        Guid PublishedAnnouncementId,
        Instant PublishedUtc);
}
