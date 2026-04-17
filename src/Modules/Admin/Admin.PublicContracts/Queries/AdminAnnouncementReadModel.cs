namespace Admin.PublicContracts.Queries;

public sealed record AdminAnnouncementReadModel(
    Guid AnnouncementId,
    string Title,
    string Body,
    DateTimeOffset PublishedUtc,
    string PublishedByActorId)
{
    public string? SourceModuleKey { get; init; }

    public string? SourceReference { get; init; }
}
