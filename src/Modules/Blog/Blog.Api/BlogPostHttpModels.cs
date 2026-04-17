using Blog.Application.Posts;
using Blog.Application.Scheduling;
using Blog.Application.Taxonomy;
using Blog.Domain.Posts;
using Blog.Domain.Taxonomy;
using BuildingBlocks.Application;
using BuildingBlocks.Application.Results;
using NodaTime;
using NodaTime.Text;

namespace Blog.Api;

public sealed record BlogSeoMetadataRequest(
    string? Title,
    string? Description,
    string? Keywords)
{
    public BlogSeoMetadata ToDomain()
    {
        return new BlogSeoMetadata(Title, Description, Keywords);
    }
}

public sealed record CreateBlogPostRequest(
    string? Slug,
    string Title,
    string Summary,
    string Body,
    bool Featured,
    string? CategorySlug,
    string[]? TagNames,
    string[]? ShareTargets,
    BlogSeoMetadataRequest? SeoMetadata);

public sealed record UpdateBlogPostRequest(
    int ExpectedVersion,
    string? Slug,
    string Title,
    string Summary,
    string Body,
    bool Featured,
    string? CategorySlug,
    string[]? TagNames,
    string[]? ShareTargets,
    BlogSeoMetadataRequest? SeoMetadata);

public sealed record PublishBlogPostRequest(int ExpectedVersion);

public sealed record UpdateBlogPostStatusRequest(int ExpectedVersion, string Status);

public sealed record CreateBlogCategoryRequest(string? Slug, string Name, string? Description);

public sealed record UpdateBlogCategoryRequest(int ExpectedVersion, string Name, string? Description);

public sealed record CreateBlogTagRequest(string? Slug, string DisplayName, string? Description);

public sealed record UpdateBlogTagRequest(int ExpectedVersion, string DisplayName, string? Description);

public sealed record ScheduleBlogPostLifecycleRequest(
    int ExpectedVersion,
    string? PublishLocalDate,
    string? PublishLocalTime,
    string? UnpublishLocalDate,
    string? UnpublishLocalTime)
{
    private static readonly LocalTimePattern HourMinutePattern = LocalTimePattern.CreateWithInvariantCulture("HH':'mm");

    private static readonly Error InvalidPublishLocalDate = new(
        "blog.invalid_publish_local_date",
        "Scheduled publish local date must use the ISO format yyyy-MM-dd.",
        ErrorKind.Validation);

    private static readonly Error InvalidPublishLocalTime = new(
        "blog.invalid_publish_local_time",
        "Scheduled publish local time must use the 24-hour format HH:mm.",
        ErrorKind.Validation);

    private static readonly Error InvalidUnpublishLocalDate = new(
        "blog.invalid_unpublish_local_date",
        "Scheduled unpublish local date must use the ISO format yyyy-MM-dd.",
        ErrorKind.Validation);

    private static readonly Error InvalidUnpublishLocalTime = new(
        "blog.invalid_unpublish_local_time",
        "Scheduled unpublish local time must use the 24-hour format HH:mm.",
        ErrorKind.Validation);

    public Result<ScheduleBlogPostLifecycleCommand> ToCommand(Guid postId)
    {
        var publishDate = ParseOptionalDate(PublishLocalDate, InvalidPublishLocalDate);
        if (publishDate.IsFailure)
        {
            return Result<ScheduleBlogPostLifecycleCommand>.Failure(publishDate.Error);
        }

        var publishTime = ParseOptionalTime(PublishLocalTime, InvalidPublishLocalTime);
        if (publishTime.IsFailure)
        {
            return Result<ScheduleBlogPostLifecycleCommand>.Failure(publishTime.Error);
        }

        var unpublishDate = ParseOptionalDate(UnpublishLocalDate, InvalidUnpublishLocalDate);
        if (unpublishDate.IsFailure)
        {
            return Result<ScheduleBlogPostLifecycleCommand>.Failure(unpublishDate.Error);
        }

        var unpublishTime = ParseOptionalTime(UnpublishLocalTime, InvalidUnpublishLocalTime);
        if (unpublishTime.IsFailure)
        {
            return Result<ScheduleBlogPostLifecycleCommand>.Failure(unpublishTime.Error);
        }

        return Result<ScheduleBlogPostLifecycleCommand>.Success(
            new ScheduleBlogPostLifecycleCommand(
                postId,
                ExpectedVersion,
                publishDate.Value,
                publishTime.Value,
                unpublishDate.Value,
                unpublishTime.Value));
    }

    private static Result<LocalDate?> ParseOptionalDate(string? value, Error error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<LocalDate?>.Success(null);
        }

        var parsed = LocalDatePattern.Iso.Parse(value.Trim());
        return parsed.Success
            ? Result<LocalDate?>.Success(parsed.Value)
            : Result<LocalDate?>.Failure(error);
    }

    private static Result<LocalTime?> ParseOptionalTime(string? value, Error error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<LocalTime?>.Success(null);
        }

        var trimmed = value.Trim();
        var parsed = HourMinutePattern.Parse(trimmed);
        if (!parsed.Success)
        {
            parsed = LocalTimePattern.ExtendedIso.Parse(trimmed);
        }

        return parsed.Success
            ? Result<LocalTime?>.Success(parsed.Value)
            : Result<LocalTime?>.Failure(error);
    }
}

public sealed record BlogSeoMetadataResponse(
    string? Title,
    string? Description,
    string? Keywords)
{
    public static BlogSeoMetadataResponse From(BlogSeoMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new BlogSeoMetadataResponse(
            metadata.Title,
            metadata.Description,
            metadata.Keywords);
    }
}

public sealed record BlogPostListResponse(IReadOnlyCollection<BlogPostResponse> Posts)
{
    public static BlogPostListResponse From(BlogPostCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return new BlogPostListResponse(collection.Posts.Select(BlogPostResponse.From).ToArray());
    }
}

public sealed record BlogPostPagedListResponse(IReadOnlyCollection<BlogPostResponse> Posts, string? NextCursor)
{
    public static BlogPostPagedListResponse From(CursorPagedResult<BlogPost> page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new BlogPostPagedListResponse(
            page.Items.Select(BlogPostResponse.From).ToArray(),
            page.NextCursor);
    }
}

public sealed record BlogCategoryResponse(
    string Slug,
    string Name,
    string? Description,
    int Version,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string UpdatedByActorId)
{
    public static BlogCategoryResponse From(BlogCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return new BlogCategoryResponse(
            category.Slug,
            category.Name,
            category.Description,
            category.Version,
            category.CreatedUtc.ToDateTimeOffset(),
            category.UpdatedUtc.ToDateTimeOffset(),
            category.UpdatedByActorId);
    }
}

public sealed record BlogCategoryListResponse(IReadOnlyCollection<BlogCategoryResponse> Categories)
{
    public static BlogCategoryListResponse From(BlogCategoryCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return new BlogCategoryListResponse(collection.Categories.Select(BlogCategoryResponse.From).ToArray());
    }
}

public sealed record BlogTagResponse(
    string Slug,
    string DisplayName,
    string? Description,
    int Version,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string UpdatedByActorId)
{
    public static BlogTagResponse From(BlogTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return new BlogTagResponse(
            tag.Slug,
            tag.DisplayName,
            tag.Description,
            tag.Version,
            tag.CreatedUtc.ToDateTimeOffset(),
            tag.UpdatedUtc.ToDateTimeOffset(),
            tag.UpdatedByActorId);
    }
}

public sealed record BlogTagListResponse(IReadOnlyCollection<BlogTagResponse> Tags)
{
    public static BlogTagListResponse From(BlogTagCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return new BlogTagListResponse(collection.Tags.Select(BlogTagResponse.From).ToArray());
    }
}

public sealed record BlogScheduledPublicationResponse(
    DateOnly ScheduledLocalDate,
    TimeOnly ScheduledLocalTime,
    string TimeZoneId,
    string LocalTimeResolution,
    DateTimeOffset ScheduledForUtc,
    string ScheduledByActorId)
{
    public static BlogScheduledPublicationResponse From(BlogScheduledPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);

        return new BlogScheduledPublicationResponse(
            new DateOnly(publication.ScheduledLocalDate.Year, publication.ScheduledLocalDate.Month, publication.ScheduledLocalDate.Day),
            new TimeOnly(publication.ScheduledLocalTime.Hour, publication.ScheduledLocalTime.Minute, publication.ScheduledLocalTime.Second, publication.ScheduledLocalTime.Millisecond),
            publication.TimeZoneId,
            publication.LocalTimeResolution,
            publication.ScheduledForUtc.ToDateTimeOffset(),
            publication.ScheduledByActorId);
    }
}

public sealed record BlogPostResponse(
    Guid PostId,
    string Slug,
    string Title,
    string Summary,
    string Body,
    bool Featured,
    string Status,
    int Version,
    int RevisionNumber,
    string? CategorySlug,
    IReadOnlyCollection<string> TagNames,
    BlogSeoMetadataResponse SeoMetadata,
    IReadOnlyCollection<string> ShareTargets,
    long ViewCount,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string UpdatedByActorId,
    DateTimeOffset? PublishedUtc,
    string? PublishedByActorId,
    BlogScheduledPublicationResponse? ScheduledPublish,
    BlogScheduledPublicationResponse? ScheduledUnpublish)
{
    public static BlogPostResponse From(BlogPost post)
    {
        ArgumentNullException.ThrowIfNull(post);

        return new BlogPostResponse(
            post.PostId,
            post.Slug,
            post.Title,
            post.Summary,
            post.Body,
            post.Featured,
            BlogPostStatusNames.From(post.Status),
            post.Version,
            post.RevisionNumber,
            post.CategorySlug,
            post.TagNames.ToArray(),
            BlogSeoMetadataResponse.From(post.SeoMetadata),
            post.ShareTargets.ToArray(),
            post.ViewCount,
            post.CreatedUtc.ToDateTimeOffset(),
            post.UpdatedUtc.ToDateTimeOffset(),
            post.UpdatedByActorId,
            post.PublishedUtc?.ToDateTimeOffset(),
            post.PublishedByActorId,
            post.PublicationSchedule.Publish is null ? null : BlogScheduledPublicationResponse.From(post.PublicationSchedule.Publish),
            post.PublicationSchedule.Unpublish is null ? null : BlogScheduledPublicationResponse.From(post.PublicationSchedule.Unpublish));
    }
}
