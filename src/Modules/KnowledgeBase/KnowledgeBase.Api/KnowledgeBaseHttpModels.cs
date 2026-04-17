using BuildingBlocks.Application;
using KnowledgeBase.Application.Entries;
using KnowledgeBase.Application.Settings;
using KnowledgeBase.Domain.Entries;

namespace KnowledgeBase.Api;

public sealed record CreateKnowledgeEntryRequest(
    string? Slug,
    string Title,
    string Body,
    string? Category,
    bool Featured,
    int SortOrder);

public sealed record UpdateKnowledgeEntryRequest(
    int ExpectedVersion,
    string? Slug,
    string Title,
    string Body,
    string? Category,
    bool Featured,
    int SortOrder);

public sealed record PublishKnowledgeEntryRequest(int ExpectedVersion);

public sealed record UpdateKnowledgeEntryStatusRequest(int ExpectedVersion, string Status);

public sealed record UpdateKnowledgeBaseSettingsRequest(
    int ExpectedVersion,
    string PublicExperienceTitle,
    string PublicExperienceBlurb,
    string SearchPlaceholder,
    bool SearchEnabled,
    int ManagementPreviewLimit);

public sealed record KnowledgeEntryListResponse(IReadOnlyCollection<KnowledgeEntryResponse> Entries)
{
    public static KnowledgeEntryListResponse From(KnowledgeEntryCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return new KnowledgeEntryListResponse(collection.Entries.Select(KnowledgeEntryResponse.From).ToArray());
    }
}

public sealed record KnowledgeEntryPagedListResponse(IReadOnlyCollection<KnowledgeEntryResponse> Entries, string? NextCursor)
{
    public static KnowledgeEntryPagedListResponse From(CursorPagedResult<KnowledgeEntry> page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new KnowledgeEntryPagedListResponse(
            page.Items.Select(KnowledgeEntryResponse.From).ToArray(),
            page.NextCursor);
    }
}

public sealed record KnowledgeEntryResponse(
    Guid EntryId,
    string Slug,
    string Title,
    string Body,
    string Category,
    bool Featured,
    int SortOrder,
    string Status,
    int Version,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string UpdatedByActorId,
    DateTimeOffset? PublishedUtc,
    string? PublishedByActorId)
{
    public static KnowledgeEntryResponse From(KnowledgeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new KnowledgeEntryResponse(
            entry.EntryId,
            entry.Slug,
            entry.Title,
            entry.Body,
            entry.Category,
            entry.Featured,
            entry.SortOrder,
            KnowledgeEntryStatusNames.From(entry.Status),
            entry.Version,
            entry.CreatedUtc.ToDateTimeOffset(),
            entry.UpdatedUtc.ToDateTimeOffset(),
            entry.UpdatedByActorId,
            entry.PublishedUtc?.ToDateTimeOffset(),
            entry.PublishedByActorId);
    }
}

public sealed record KnowledgeBasePublicSettingsResponse(
    string PublicExperienceTitle,
    string PublicExperienceBlurb,
    string SearchPlaceholder,
    bool SearchEnabled)
{
    public static KnowledgeBasePublicSettingsResponse From(KnowledgeBaseModuleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new KnowledgeBasePublicSettingsResponse(
            settings.PublicExperienceTitle,
            settings.PublicExperienceBlurb,
            settings.SearchPlaceholder,
            settings.SearchEnabled);
    }
}

public sealed record KnowledgeBaseManagementSettingsResponse(
    string PublicExperienceTitle,
    string PublicExperienceBlurb,
    string SearchPlaceholder,
    bool SearchEnabled,
    int ManagementPreviewLimit,
    int Version,
    DateTimeOffset UpdatedUtc,
    string UpdatedByActorId)
{
    public static KnowledgeBaseManagementSettingsResponse From(KnowledgeBaseModuleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new KnowledgeBaseManagementSettingsResponse(
            settings.PublicExperienceTitle,
            settings.PublicExperienceBlurb,
            settings.SearchPlaceholder,
            settings.SearchEnabled,
            settings.ManagementPreviewLimit,
            settings.Version,
            settings.UpdatedUtc.ToDateTimeOffset(),
            settings.UpdatedByActorId);
    }
}
