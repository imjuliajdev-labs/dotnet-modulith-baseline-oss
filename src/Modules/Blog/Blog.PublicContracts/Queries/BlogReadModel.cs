namespace Blog.PublicContracts.Queries;

public sealed record BlogReadModel(
    Guid PostId,
    string Slug,
    string Title,
    string Summary,
    DateTimeOffset PublishedUtc);
