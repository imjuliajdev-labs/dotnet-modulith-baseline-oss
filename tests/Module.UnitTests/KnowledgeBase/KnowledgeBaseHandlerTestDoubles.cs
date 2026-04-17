using BuildingBlocks.Application;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using KnowledgeBase.Application.Entries;
using KnowledgeBase.Application.Settings;
using KnowledgeBase.Domain.Entries;
using NodaTime;

namespace Module.UnitTests.KnowledgeBase;

internal static class KnowledgeBaseTestData
{
    public static readonly Instant FixedNow = Instant.FromUtc(2026, 4, 5, 14, 0);

    public static KnowledgeEntry CreateEntry(
        KnowledgeEntryStatus status,
        int version,
        Instant? publishedUtc = null,
        Guid? entryId = null,
        string slug = "entry-title",
        string title = "Entry Title",
        string body = "Entry Body",
        string category = "Operations",
        bool featured = true,
        int sortOrder = 25,
        string updatedByActorId = "operator-1")
    {
        return new KnowledgeEntry(
            entryId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
            slug,
            title,
            body,
            category,
            featured,
            sortOrder,
            status,
            version,
            FixedNow,
            FixedNow,
            updatedByActorId,
            publishedUtc,
            publishedUtc is null ? null : updatedByActorId);
    }

    public static KnowledgeBaseModuleSettings CreateSettings(
        string publicExperienceTitle = "Knowledge Base",
        string publicExperienceBlurb = "Help content for operators.",
        string searchPlaceholder = "Search the knowledge base",
        bool searchEnabled = true,
        int managementPreviewLimit = 12,
        int version = 3,
        Instant? updatedUtc = null,
        string updatedByActorId = "operator-1")
    {
        return new KnowledgeBaseModuleSettings(
            publicExperienceTitle,
            publicExperienceBlurb,
            searchPlaceholder,
            searchEnabled,
            managementPreviewLimit,
            version,
            updatedUtc ?? FixedNow,
            updatedByActorId);
    }
}

internal sealed class RecordingKnowledgeBaseStore : IKnowledgeBaseStore
{
    public Result<KnowledgeEntry> CreateResult { get; set; } = Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.EntryNotFound(Guid.Empty));

    public Result<KnowledgeEntry> GetByIdResult { get; set; } = Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.EntryNotFound(Guid.Empty));

    public Result<KnowledgeEntry> PublishedBySlugResult { get; set; } = Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.PublishedEntryNotFound(string.Empty));

    public Result<KnowledgeEntry> SetStatusResult { get; set; } = Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.EntryNotFound(Guid.Empty));

    public Result<KnowledgeEntry> UpdateResult { get; set; } = Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.EntryNotFound(Guid.Empty));

    public IReadOnlyCollection<KnowledgeEntry> PublishedEntries { get; set; } = [];

    public IReadOnlyCollection<KnowledgeEntry> AllEntries { get; set; } = [];

    public string? LastCreateSlug { get; private set; }

    public string? LastCreateTitle { get; private set; }

    public string? LastCreateBody { get; private set; }

    public string? LastCreateCategory { get; private set; }

    public bool LastCreateFeatured { get; private set; }

    public int LastCreateSortOrder { get; private set; }

    public string? LastCreateActorId { get; private set; }

    public Instant LastCreateNow { get; private set; }

    public Guid LastGetByIdEntryId { get; private set; }

    public Guid LastUpdateEntryId { get; private set; }

    public int LastUpdateExpectedVersion { get; private set; }

    public string? LastUpdateSlug { get; private set; }

    public string? LastUpdateTitle { get; private set; }

    public string? LastUpdateBody { get; private set; }

    public string? LastUpdateCategory { get; private set; }

    public bool LastUpdateFeatured { get; private set; }

    public int LastUpdateSortOrder { get; private set; }

    public string? LastUpdateActorId { get; private set; }

    public Instant LastUpdateNow { get; private set; }

    public string? LastPublishedSlugLookup { get; private set; }

    public int LastPublishedLimit { get; private set; }

    public string? LastAfterCursor { get; private set; }

    public int SetStatusCalls { get; private set; }

    public Guid LastSetStatusEntryId { get; private set; }

    public int LastSetStatusExpectedVersion { get; private set; }

    public KnowledgeEntryStatus LastSetStatus { get; private set; }

    public string? LastSetStatusActorId { get; private set; }

    public Instant LastSetStatusNow { get; private set; }

    public ValueTask<IReadOnlyCollection<KnowledgeEntry>> ListPublishedAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(PublishedEntries);
    }

    public ValueTask<CursorPagedResult<KnowledgeEntry>> ListPublishedPagedAsync(int limit, string? afterCursor, CancellationToken cancellationToken)
    {
        LastPublishedLimit = limit;
        LastAfterCursor = afterCursor;
        var items = PublishedEntries.Take(limit).ToArray();
        return ValueTask.FromResult(new CursorPagedResult<KnowledgeEntry>(items, null));
    }

    public ValueTask<Result<KnowledgeEntry>> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        LastPublishedSlugLookup = slug;
        return ValueTask.FromResult(PublishedBySlugResult);
    }

    public ValueTask<Result<KnowledgeEntry>> GetByIdAsync(Guid entryId, CancellationToken cancellationToken)
    {
        LastGetByIdEntryId = entryId;
        return ValueTask.FromResult(GetByIdResult);
    }

    public ValueTask<IReadOnlyCollection<KnowledgeEntry>> ListAllAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(AllEntries);
    }

    public ValueTask<Result<KnowledgeEntry>> CreateAsync(
        string slug,
        string title,
        string body,
        string category,
        bool featured,
        int sortOrder,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastCreateSlug = slug;
        LastCreateTitle = title;
        LastCreateBody = body;
        LastCreateCategory = category;
        LastCreateFeatured = featured;
        LastCreateSortOrder = sortOrder;
        LastCreateActorId = actorId;
        LastCreateNow = now;

        return ValueTask.FromResult(CreateResult);
    }

    public ValueTask<Result<KnowledgeEntry>> UpdateAsync(
        Guid entryId,
        int expectedVersion,
        string slug,
        string title,
        string body,
        string category,
        bool featured,
        int sortOrder,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastUpdateEntryId = entryId;
        LastUpdateExpectedVersion = expectedVersion;
        LastUpdateSlug = slug;
        LastUpdateTitle = title;
        LastUpdateBody = body;
        LastUpdateCategory = category;
        LastUpdateFeatured = featured;
        LastUpdateSortOrder = sortOrder;
        LastUpdateActorId = actorId;
        LastUpdateNow = now;

        return ValueTask.FromResult(UpdateResult);
    }

    public ValueTask<Result<KnowledgeEntry>> SetStatusAsync(
        Guid entryId,
        int expectedVersion,
        KnowledgeEntryStatus status,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        SetStatusCalls += 1;
        LastSetStatusEntryId = entryId;
        LastSetStatusExpectedVersion = expectedVersion;
        LastSetStatus = status;
        LastSetStatusActorId = actorId;
        LastSetStatusNow = now;
        return ValueTask.FromResult(SetStatusResult);
    }
}

internal sealed class RecordingKnowledgeBaseSettingsStore : IKnowledgeBaseSettingsStore
{
    public KnowledgeBaseModuleSettings CurrentSettings { get; set; } = KnowledgeBaseTestData.CreateSettings();

    public Result<KnowledgeBaseModuleSettings> UpdateResult { get; set; } = Result<KnowledgeBaseModuleSettings>.Success(KnowledgeBaseTestData.CreateSettings());

    public int UpdateCalls { get; private set; }

    public int LastExpectedVersion { get; private set; }

    public NormalizedKnowledgeBaseSettings? LastSettings { get; private set; }

    public string? LastActorId { get; private set; }

    public Instant LastUpdatedUtc { get; private set; }

    public ValueTask<KnowledgeBaseModuleSettings> GetAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(CurrentSettings);
    }

    public ValueTask<Result<KnowledgeBaseModuleSettings>> UpdateAsync(
        int expectedVersion,
        NormalizedKnowledgeBaseSettings settings,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        UpdateCalls += 1;
        LastExpectedVersion = expectedVersion;
        LastSettings = settings;
        LastActorId = actorId;
        LastUpdatedUtc = now;
        return ValueTask.FromResult(UpdateResult);
    }
}

internal sealed class RecordingKnowledgeBaseOutboxPublisher : IIntegrationEventOutboxPublisher
{
    public List<IntegrationEventOutboxPublishRequest> Requests { get; } = [];

    public ValueTask PublishAsync(IntegrationEventOutboxPublishRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return ValueTask.CompletedTask;
    }
}
