using Admin.PublicContracts.Queries;

namespace Admin.Application.Realtime;

public static class AdminRealtimeChannels
{
    public const string AnnouncementProjected = "admin.announcement.projected";
}

public sealed record AdminAnnouncementProjectedNotification(
    Guid AnnouncementId,
    string Title,
    string Body,
    DateTimeOffset PublishedUtc,
    string PublishedByActorId)
{
    public string? SourceModuleKey { get; init; }

    public string? SourceReference { get; init; }

    public static AdminAnnouncementProjectedNotification From(AdminAnnouncementReadModel announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        return new AdminAnnouncementProjectedNotification(
            announcement.AnnouncementId,
            announcement.Title,
            announcement.Body,
            announcement.PublishedUtc,
            announcement.PublishedByActorId)
        {
            SourceModuleKey = announcement.SourceModuleKey,
            SourceReference = announcement.SourceReference
        };
    }
}

public interface IAdminAnnouncementRealtimeNotifier
{
    Task PublishProjectedAsync(AdminAnnouncementProjectedNotification notification, CancellationToken cancellationToken);
}
