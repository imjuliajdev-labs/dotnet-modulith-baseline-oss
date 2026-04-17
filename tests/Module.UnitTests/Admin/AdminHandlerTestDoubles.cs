using Admin.Application.Consumers;
using Admin.Application.ProcessManagers;
using Admin.Application.Queries;
using Admin.Application.SharedReads;
using Admin.PublicContracts.Queries;
using KnowledgeBase.PublicContracts.Queries;

namespace Module.UnitTests.Admin;

internal static class AdminTestData
{
    public static readonly DateTimeOffset FixedPublishedUtc = DateTimeOffset.Parse("2026-04-16T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    public static AdminAnnouncementReadModel CreateAnnouncement(
        Guid? announcementId = null,
        string title = "Operational announcement",
        string body = "Body",
        DateTimeOffset? publishedUtc = null,
        string publishedByActorId = "admin-1",
        string? sourceModuleKey = null,
        string? sourceReference = null)
    {
        return new AdminAnnouncementReadModel(
            announcementId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
            title,
            body,
            publishedUtc ?? FixedPublishedUtc,
            publishedByActorId)
        {
            SourceModuleKey = sourceModuleKey,
            SourceReference = sourceReference
        };
    }

    public static KnowledgeBasePublishedEntryReadModel CreateGuidance(
        Guid? entryId = null,
        string slug = "ops-guide",
        string title = "Ops guide",
        string body = "Body",
        string category = "Operations",
        bool featured = true,
        int sortOrder = 1,
        DateTimeOffset? publishedUtc = null)
    {
        return new KnowledgeBasePublishedEntryReadModel(
            entryId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"),
            slug,
            title,
            body,
            category,
            featured,
            sortOrder,
            publishedUtc ?? FixedPublishedUtc);
    }
}

internal sealed class RecordingAdminAnnouncementQueryService : IAdminAnnouncementQueryService
{
    public AdminAnnouncementReadModel? GetResult { get; set; }

    public IReadOnlyCollection<AdminAnnouncementReadModel> ListResult { get; set; } = [];

    public Guid? LastGetId { get; private set; }

    public int? LastListLimit { get; private set; }

    public ValueTask<AdminAnnouncementReadModel?> GetAsync(Guid announcementId, CancellationToken cancellationToken)
    {
        LastGetId = announcementId;
        return ValueTask.FromResult(GetResult);
    }

    public ValueTask<IReadOnlyCollection<AdminAnnouncementReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        LastListLimit = limit;
        return ValueTask.FromResult(ListResult);
    }
}

internal sealed class RecordingAdminKnowledgeBaseGuidanceReader : IAdminKnowledgeBaseGuidanceReader
{
    public IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel> Result { get; set; } = [];

    public int? LastLimit { get; private set; }

    public ValueTask<IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        LastLimit = limit;
        return ValueTask.FromResult(Result);
    }
}

internal sealed class RecordingAdminAnnouncementProjectionProcessManager : IAdminAnnouncementProjectionProcessManager
{
    public List<AdminAnnouncementProjection> Projections { get; } = [];

    public Task HandleAsync(AdminAnnouncementProjection announcement, CancellationToken cancellationToken)
    {
        Projections.Add(announcement);
        return Task.CompletedTask;
    }
}
