using Admin.PublicContracts.Queries;
using BuildingBlocks.Domain.Contracts;

namespace Admin.Api.Contracts;

[ContractLifecycle("2025-06-01", ContractLifecycleStatus.Active)]
public sealed class AdminMachineAnnouncementsResponseV1
{
    public const string Version = "v1";

    public string? ContractVersion { get; init; }

    public required IReadOnlyCollection<AdminMachineAnnouncementItemV1> Announcements { get; init; }

    public static AdminMachineAnnouncementsResponseV1 From(AdminAnnouncementListResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new AdminMachineAnnouncementsResponseV1
        {
            ContractVersion = Version,
            Announcements = response.Announcements.Select(AdminMachineAnnouncementItemV1.From).ToArray()
        };
    }
}

[ContractLifecycle("2025-06-01", ContractLifecycleStatus.Active)]
public sealed class AdminMachineAnnouncementItemV1
{
    public required Guid AnnouncementId { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public required DateTimeOffset PublishedUtc { get; init; }

    public required string PublishedByActorId { get; init; }

    public string? SourceModuleKey { get; init; }

    public string? SourceReference { get; init; }

    public static AdminMachineAnnouncementItemV1 From(AdminAnnouncementReadModel announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        return new AdminMachineAnnouncementItemV1
        {
            AnnouncementId = announcement.AnnouncementId,
            Title = announcement.Title,
            Body = announcement.Body,
            PublishedUtc = announcement.PublishedUtc,
            PublishedByActorId = announcement.PublishedByActorId,
            SourceModuleKey = announcement.SourceModuleKey,
            SourceReference = announcement.SourceReference
        };
    }
}
