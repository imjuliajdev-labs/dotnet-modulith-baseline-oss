namespace KnowledgeBase.PublicContracts.Queries;

public sealed record KnowledgeBasePublishedEntryReadModel(
    Guid EntryId,
    string Slug,
    string Title,
    string Body,
    string Category,
    bool Featured,
    int SortOrder,
    DateTimeOffset PublishedUtc);
