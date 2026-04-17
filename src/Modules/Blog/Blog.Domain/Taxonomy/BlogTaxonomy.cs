using NodaTime;

namespace Blog.Domain.Taxonomy;

public sealed record BlogCategory(
    string Slug,
    string Name,
    string? Description,
    int Version,
    Instant CreatedUtc,
    Instant UpdatedUtc,
    string UpdatedByActorId);

public sealed record BlogTag(
    string Slug,
    string DisplayName,
    string? Description,
    int Version,
    Instant CreatedUtc,
    Instant UpdatedUtc,
    string UpdatedByActorId);
