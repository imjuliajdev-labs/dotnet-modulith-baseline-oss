using Blog.Application.Posts;
using Blog.Domain.Posts;
using BuildingBlocks.Application;
using BuildingBlocks.Application.Results;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Blog.Infrastructure.Persistence;

internal sealed class BlogPostReadQueries : IBlogPostReadQueries
{
    private readonly IDbContextFactory<BlogPersistenceDbContext> _dbContextFactory;

    public BlogPostReadQueries(IDbContextFactory<BlogPersistenceDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async ValueTask<CursorPagedResult<BlogPost>> ListPublishedPagedAsync(int limit, string? afterCursor, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = BuildProjection(dbContext)
            .Where(p => p.Status == BlogPostStatus.Published);

        var decoded = CursorEncoding.Decode(afterCursor);
        if (decoded.Status == CursorDecodeStatus.Valid
            && DateTimeOffset.TryParse(decoded.SortValue, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var lastPublishedDto)
            && Guid.TryParse(decoded.Id, out var lastPostId))
        {
            Instant? lastPublished = Instant.FromDateTimeOffset(lastPublishedDto);
            query = query.Where(p =>
                p.PublishedUtc < lastPublished
                || (p.PublishedUtc == lastPublished && p.PostId.CompareTo(lastPostId) < 0));
        }

        var projections = await query
            .OrderByDescending(p => p.PublishedUtc)
            .ThenByDescending(p => p.PostId)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);

        var hasMore = projections.Length > limit;
        var page = hasMore ? projections[..limit] : projections;
        var items = page.Select(MapToDomain).ToArray();

        string? nextCursor = null;
        if (hasMore && page.Length > 0)
        {
            var last = page[^1];
            var publishedUtcString = (last.PublishedUtc?.ToDateTimeOffset() ?? DateTimeOffset.MinValue).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            nextCursor = CursorEncoding.Encode(publishedUtcString, last.PostId.ToString());
        }

        return new CursorPagedResult<BlogPost>(items, nextCursor);
    }

    public async ValueTask<Result<BlogPost>> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var projection = await BuildProjection(dbContext)
            .SingleOrDefaultAsync(
                p => p.Status == BlogPostStatus.Published && p.Slug == slug,
                cancellationToken);

        return projection is null
            ? Result<BlogPost>.Failure(BlogPostErrors.PublishedPostNotFound(slug))
            : Result<BlogPost>.Success(MapToDomain(projection));
    }

    public async ValueTask<IReadOnlyCollection<BlogPost>> ListAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var projections = await BuildProjection(dbContext)
            .OrderByDescending(p => p.Featured)
            .ThenByDescending(p => p.PublishedUtc)
            .ThenBy(p => p.Title)
            .ToArrayAsync(cancellationToken);

        return projections.Select(MapToDomain).ToArray();
    }

    private static IQueryable<BlogPostProjection> BuildProjection(BlogPersistenceDbContext dbContext)
    {
        return dbContext.Posts
            .AsNoTracking()
            .Select(post => new BlogPostProjection
            {
                PostId = post.PostId,
                Slug = post.Slug,
                Title = post.Title,
                Summary = post.Summary,
                Body = post.Body,
                Featured = post.Featured,
                Status = post.Status,
                Version = post.Version,
                RevisionNumber = post.RevisionNumber,
                CategorySlug = post.CategorySlug,
                SeoTitle = post.SeoTitle,
                SeoDescription = post.SeoDescription,
                SeoKeywords = post.SeoKeywords,
                ViewCount = post.ViewCount,
                CreatedUtc = post.CreatedUtc,
                UpdatedUtc = post.UpdatedUtc,
                UpdatedByActorId = post.UpdatedByActorId,
                PublishedUtc = post.PublishedUtc,
                PublishedByActorId = post.PublishedByActorId,
                TagNames = post.Tags
                    .Select(tag => tag.TagSlug)
                    .OrderBy(tag => tag)
                    .ToArray(),
                ShareTargets = post.ShareTargets
                    .Select(target => target.Target)
                    .OrderBy(target => target)
                    .ToArray(),
                SchedulePublishLocalDate = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledPublishLocalDate : null,
                SchedulePublishLocalTime = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledPublishLocalTime : null,
                SchedulePublishTimeZoneId = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledPublishTimeZoneId : null,
                SchedulePublishLocalTimeResolution = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledPublishLocalTimeResolution : null,
                SchedulePublishForUtc = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledPublishForUtc : null,
                SchedulePublishByActorId = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledPublishByActorId : null,
                ScheduleUnpublishLocalDate = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledUnpublishLocalDate : null,
                ScheduleUnpublishLocalTime = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledUnpublishLocalTime : null,
                ScheduleUnpublishTimeZoneId = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledUnpublishTimeZoneId : null,
                ScheduleUnpublishLocalTimeResolution = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledUnpublishLocalTimeResolution : null,
                ScheduleUnpublishForUtc = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledUnpublishForUtc : null,
                ScheduleUnpublishByActorId = post.PublicationSchedule != null ? post.PublicationSchedule.ScheduledUnpublishByActorId : null,
            });
    }

    private static BlogPost MapToDomain(BlogPostProjection p)
    {
        return new BlogPost(
            p.PostId,
            p.Slug,
            p.Title,
            p.Summary,
            p.Body,
            p.Featured,
            p.Status,
            p.Version,
            p.RevisionNumber,
            p.CategorySlug,
            p.TagNames,
            new BlogSeoMetadata(p.SeoTitle, p.SeoDescription, p.SeoKeywords),
            p.ShareTargets,
            p.ViewCount,
            p.CreatedUtc,
            p.UpdatedUtc,
            p.UpdatedByActorId,
            p.PublishedUtc,
            p.PublishedByActorId,
            MapPublicationSchedule(p));
    }

    private static BlogPublicationSchedule MapPublicationSchedule(BlogPostProjection p)
    {
        var publish = MapScheduledPublication(
            p.SchedulePublishLocalDate,
            p.SchedulePublishLocalTime,
            p.SchedulePublishTimeZoneId,
            p.SchedulePublishLocalTimeResolution,
            p.SchedulePublishForUtc,
            p.SchedulePublishByActorId);

        var unpublish = MapScheduledPublication(
            p.ScheduleUnpublishLocalDate,
            p.ScheduleUnpublishLocalTime,
            p.ScheduleUnpublishTimeZoneId,
            p.ScheduleUnpublishLocalTimeResolution,
            p.ScheduleUnpublishForUtc,
            p.ScheduleUnpublishByActorId);

        return (publish is null && unpublish is null)
            ? BlogPublicationSchedule.Empty
            : new BlogPublicationSchedule(publish, unpublish);
    }

    private static BlogScheduledPublication? MapScheduledPublication(
        LocalDate? localDate,
        LocalTime? localTime,
        string? timeZoneId,
        string? localTimeResolution,
        DateTimeOffset? scheduledForUtc,
        string? scheduledByActorId)
    {
        if (!localDate.HasValue || !localTime.HasValue || string.IsNullOrWhiteSpace(timeZoneId) || !scheduledForUtc.HasValue)
        {
            return null;
        }

        return new BlogScheduledPublication(
            localDate.Value,
            localTime.Value,
            timeZoneId,
            localTimeResolution ?? "exact",
            Instant.FromDateTimeOffset(scheduledForUtc.Value),
            scheduledByActorId ?? "unknown");
    }
}

internal sealed class BlogPostProjection
{
    public Guid PostId { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public bool Featured { get; init; }
    public BlogPostStatus Status { get; init; }
    public int Version { get; init; }
    public int RevisionNumber { get; init; }
    public string? CategorySlug { get; init; }
    public string? SeoTitle { get; init; }
    public string? SeoDescription { get; init; }
    public string? SeoKeywords { get; init; }
    public long ViewCount { get; init; }
    public Instant CreatedUtc { get; init; }
    public Instant UpdatedUtc { get; init; }
    public string UpdatedByActorId { get; init; } = string.Empty;
    public Instant? PublishedUtc { get; init; }
    public string? PublishedByActorId { get; init; }
    public string[] TagNames { get; init; } = [];
    public string[] ShareTargets { get; init; } = [];
    public LocalDate? SchedulePublishLocalDate { get; init; }
    public LocalTime? SchedulePublishLocalTime { get; init; }
    public string? SchedulePublishTimeZoneId { get; init; }
    public string? SchedulePublishLocalTimeResolution { get; init; }
    public DateTimeOffset? SchedulePublishForUtc { get; init; }
    public string? SchedulePublishByActorId { get; init; }
    public LocalDate? ScheduleUnpublishLocalDate { get; init; }
    public LocalTime? ScheduleUnpublishLocalTime { get; init; }
    public string? ScheduleUnpublishTimeZoneId { get; init; }
    public string? ScheduleUnpublishLocalTimeResolution { get; init; }
    public DateTimeOffset? ScheduleUnpublishForUtc { get; init; }
    public string? ScheduleUnpublishByActorId { get; init; }
}
