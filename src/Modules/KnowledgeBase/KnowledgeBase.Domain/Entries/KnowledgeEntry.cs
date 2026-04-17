using NodaTime;

namespace KnowledgeBase.Domain.Entries;

public enum KnowledgeEntryStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}

public sealed record KnowledgeEntry(
    Guid EntryId,
    string Slug,
    string Title,
    string Body,
    string Category,
    bool Featured,
    int SortOrder,
    KnowledgeEntryStatus Status,
    int Version,
    Instant CreatedUtc,
    Instant UpdatedUtc,
    string UpdatedByActorId,
    Instant? PublishedUtc,
    string? PublishedByActorId)
{
    public bool IsPublished => Status == KnowledgeEntryStatus.Published;

    public bool IsDraft => Status == KnowledgeEntryStatus.Draft;

    public bool IsArchived => Status == KnowledgeEntryStatus.Archived;

    /// <summary>
    /// Creates a new draft entry with version 1 and no publication timestamps.
    /// </summary>
    public static KnowledgeEntry CreateDraft(
        Guid entryId,
        string slug,
        string title,
        string body,
        string category,
        bool featured,
        int sortOrder,
        string actorId,
        Instant now)
    {
        return new KnowledgeEntry(
            entryId,
            slug,
            title,
            body,
            category,
            featured,
            sortOrder,
            KnowledgeEntryStatus.Draft,
            Version: 1,
            CreatedUtc: now,
            UpdatedUtc: now,
            UpdatedByActorId: actorId,
            PublishedUtc: null,
            PublishedByActorId: null);
    }

    /// <summary>
    /// Returns an updated copy with incremented version and fresh edit metadata.
    /// </summary>
    public KnowledgeEntry WithEdits(
        string slug,
        string title,
        string body,
        string category,
        bool featured,
        int sortOrder,
        string actorId,
        Instant now)
    {
        return this with
        {
            Slug = slug,
            Title = title,
            Body = body,
            Category = category,
            Featured = featured,
            SortOrder = sortOrder,
            Version = Version + 1,
            UpdatedUtc = now,
            UpdatedByActorId = actorId
        };
    }

    /// <summary>
    /// Returns a copy transitioned to the given status with version incremented.
    /// Publication timestamps are set or cleared based on the target status.
    /// </summary>
    public KnowledgeEntry WithStatus(KnowledgeEntryStatus newStatus, string actorId, Instant now)
    {
        return this with
        {
            Status = newStatus,
            Version = Version + 1,
            UpdatedUtc = now,
            UpdatedByActorId = actorId,
            PublishedUtc = newStatus == KnowledgeEntryStatus.Published
                ? now
                : newStatus == KnowledgeEntryStatus.Draft
                    ? null
                    : PublishedUtc,
            PublishedByActorId = newStatus == KnowledgeEntryStatus.Published
                ? actorId
                : newStatus == KnowledgeEntryStatus.Draft
                    ? null
                    : PublishedByActorId
        };
    }

    /// <summary>
    /// Returns true when the entry can transition to the given status.
    /// </summary>
    public bool CanTransitionTo(KnowledgeEntryStatus target)
    {
        return (Status, target) switch
        {
            (KnowledgeEntryStatus.Draft, KnowledgeEntryStatus.Published) => true,
            (KnowledgeEntryStatus.Draft, KnowledgeEntryStatus.Archived) => true,
            (KnowledgeEntryStatus.Published, KnowledgeEntryStatus.Draft) => true,
            (KnowledgeEntryStatus.Published, KnowledgeEntryStatus.Archived) => true,
            (KnowledgeEntryStatus.Archived, KnowledgeEntryStatus.Draft) => true,
            _ => false
        };
    }
}
