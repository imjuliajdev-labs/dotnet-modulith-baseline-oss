using Blog.Application.Posts;
using Blog.Application.Scheduling;
using Blog.Application.Settings;
using Blog.Application.SharedReads;
using Blog.Application.Taxonomy;
using Blog.Domain.Posts;
using Blog.Domain.Taxonomy;
using BuildingBlocks.Application;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using NodaTime;

namespace Module.UnitTests.Blog;

internal static class BlogTestData
{
    public static readonly Instant FixedNow = Instant.FromUtc(2026, 4, 16, 9, 0);

    public static BlogPost CreatePost(
        Guid? postId = null,
        string slug = "governed-blog-post",
        string title = "Governed blog post",
        string summary = "A governed summary.",
        string body = "A governed body.",
        bool featured = false,
        BlogPostStatus status = BlogPostStatus.Draft,
        int version = 1,
        int revisionNumber = 1,
        string? categorySlug = "platform",
        IReadOnlyCollection<string>? tagNames = null,
        BlogSeoMetadata? seoMetadata = null,
        IReadOnlyCollection<string>? shareTargets = null,
        long viewCount = 0,
        Instant? createdUtc = null,
        Instant? updatedUtc = null,
        string updatedByActorId = "admin-1",
        Instant? publishedUtc = null,
        string? publishedByActorId = null,
        BlogPublicationSchedule? publicationSchedule = null)
    {
        return new BlogPost(
            postId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
            slug,
            title,
            summary,
            body,
            featured,
            status,
            version,
            revisionNumber,
            categorySlug,
            tagNames ?? ["architecture"],
            seoMetadata ?? new BlogSeoMetadata("SEO title", "SEO description", "keywords"),
            shareTargets ?? ["linkedin"],
            viewCount,
            createdUtc ?? FixedNow,
            updatedUtc ?? FixedNow,
            updatedByActorId,
            publishedUtc,
            publishedByActorId,
            publicationSchedule ?? BlogPublicationSchedule.Empty);
    }

    public static BlogCategory CreateCategory(
        string slug = "platform",
        string name = "Platform",
        string? description = "Platform topics",
        int version = 1,
        Instant? createdUtc = null,
        Instant? updatedUtc = null,
        string updatedByActorId = "admin-1")
    {
        return new BlogCategory(
            slug,
            name,
            description,
            version,
            createdUtc ?? FixedNow,
            updatedUtc ?? FixedNow,
            updatedByActorId);
    }

    public static BlogTag CreateTag(
        string slug = "architecture",
        string displayName = "Architecture",
        string? description = "Architecture topics",
        int version = 1,
        Instant? createdUtc = null,
        Instant? updatedUtc = null,
        string updatedByActorId = "admin-1")
    {
        return new BlogTag(
            slug,
            displayName,
            description,
            version,
            createdUtc ?? FixedNow,
            updatedUtc ?? FixedNow,
            updatedByActorId);
    }

    public static BlogModuleSettings CreateSettings(
        string operatorSummary = "Operator summary",
        int previewLimit = 12,
        int version = 2,
        Instant? updatedUtc = null,
        string updatedByActorId = "admin-1")
    {
        return new BlogModuleSettings(
            operatorSummary,
            previewLimit,
            version,
            updatedUtc ?? FixedNow,
            updatedByActorId);
    }
}

internal sealed class RecordingBlogPostStore : IBlogPostStore
{
    public Result<BlogPost> GetByIdResult { get; set; } = Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(Guid.Empty));

    public Result<BlogPost> CreateResult { get; set; } = Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(Guid.Empty));

    public Result<BlogPost> UpdateResult { get; set; } = Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(Guid.Empty));

    public Result<BlogPost> SetStatusResult { get; set; } = Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(Guid.Empty));

    public Result<BlogPost> PublishResult { get; set; } = Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(Guid.Empty));

    public Result<BlogPost> SchedulePublicationResult { get; set; } = Result<BlogPost>.Failure(BlogPostErrors.PostNotFound(Guid.Empty));

    public IReadOnlyCollection<ProcessedBlogPostTransition> ProcessDueTransitionsResult { get; set; } = [];

    public Result RegisterPublishedViewResult { get; set; } = Result.Success();

    public Guid LastGetByIdPostId { get; private set; }

    public BlogPostDraft? LastCreateDraft { get; private set; }

    public bool LastCreateFeatured { get; private set; }

    public string? LastCreateActorId { get; private set; }

    public Instant LastCreateNow { get; private set; }

    public Guid LastUpdatePostId { get; private set; }

    public int LastUpdateExpectedVersion { get; private set; }

    public BlogPostDraft? LastUpdateDraft { get; private set; }

    public bool LastUpdateFeatured { get; private set; }

    public string? LastUpdateActorId { get; private set; }

    public Instant LastUpdateNow { get; private set; }

    public Guid LastSetStatusPostId { get; private set; }

    public int LastSetStatusExpectedVersion { get; private set; }

    public BlogPostStatus LastSetStatus { get; private set; }

    public string? LastSetStatusActorId { get; private set; }

    public Instant LastSetStatusNow { get; private set; }

    public Guid LastPublishPostId { get; private set; }

    public int LastPublishExpectedVersion { get; private set; }

    public string? LastPublishActorId { get; private set; }

    public Instant LastPublishNow { get; private set; }

    public Guid LastSchedulePostId { get; private set; }

    public int LastScheduleExpectedVersion { get; private set; }

    public BlogPublicationSchedule? LastPublicationSchedule { get; private set; }

    public string? LastScheduleActorId { get; private set; }

    public Instant LastScheduleNow { get; private set; }

    public Instant LastProcessDueNow { get; private set; }

    public int LastProcessDueBatchSize { get; private set; }

    public string? LastViewedSlug { get; private set; }

    public ValueTask<Result<BlogPost>> GetByIdAsync(Guid postId, CancellationToken cancellationToken)
    {
        LastGetByIdPostId = postId;
        return ValueTask.FromResult(GetByIdResult);
    }

    public ValueTask<Result<BlogPost>> CreateAsync(
        BlogPostDraft draft,
        bool featured,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastCreateDraft = draft;
        LastCreateFeatured = featured;
        LastCreateActorId = actorId;
        LastCreateNow = now;
        return ValueTask.FromResult(CreateResult);
    }

    public ValueTask<Result<BlogPost>> UpdateAsync(
        Guid postId,
        int expectedVersion,
        BlogPostDraft draft,
        bool featured,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastUpdatePostId = postId;
        LastUpdateExpectedVersion = expectedVersion;
        LastUpdateDraft = draft;
        LastUpdateFeatured = featured;
        LastUpdateActorId = actorId;
        LastUpdateNow = now;
        return ValueTask.FromResult(UpdateResult);
    }

    public ValueTask<Result<BlogPost>> SetStatusAsync(
        Guid postId,
        int expectedVersion,
        BlogPostStatus status,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastSetStatusPostId = postId;
        LastSetStatusExpectedVersion = expectedVersion;
        LastSetStatus = status;
        LastSetStatusActorId = actorId;
        LastSetStatusNow = now;
        return ValueTask.FromResult(SetStatusResult);
    }

    public ValueTask<Result<BlogPost>> PublishAsync(
        Guid postId,
        int expectedVersion,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastPublishPostId = postId;
        LastPublishExpectedVersion = expectedVersion;
        LastPublishActorId = actorId;
        LastPublishNow = now;
        return ValueTask.FromResult(PublishResult);
    }

    public ValueTask<Result<BlogPost>> SchedulePublicationAsync(
        Guid postId,
        int expectedVersion,
        BlogPublicationSchedule publicationSchedule,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastSchedulePostId = postId;
        LastScheduleExpectedVersion = expectedVersion;
        LastPublicationSchedule = publicationSchedule;
        LastScheduleActorId = actorId;
        LastScheduleNow = now;
        return ValueTask.FromResult(SchedulePublicationResult);
    }

    public ValueTask<IReadOnlyCollection<ProcessedBlogPostTransition>> ProcessDueScheduledTransitionsAsync(
        Instant now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        LastProcessDueNow = now;
        LastProcessDueBatchSize = batchSize;
        return ValueTask.FromResult(ProcessDueTransitionsResult);
    }

    public ValueTask<Result> RegisterPublishedViewAsync(string slug, CancellationToken cancellationToken)
    {
        LastViewedSlug = slug;
        return ValueTask.FromResult(RegisterPublishedViewResult);
    }
}

internal sealed class RecordingBlogPostReadQueries : IBlogPostReadQueries
{
    public CursorPagedResult<BlogPost> ListPublishedResult { get; set; } = new([], null);

    public Result<BlogPost> PublishedBySlugResult { get; set; } = Result<BlogPost>.Failure(BlogPostErrors.PublishedPostNotFound(string.Empty));

    public IReadOnlyCollection<BlogPost> AllPosts { get; set; } = [];

    public int LastPublishedLimit { get; private set; }

    public string? LastAfterCursor { get; private set; }

    public string? LastSlugLookup { get; private set; }

    public ValueTask<CursorPagedResult<BlogPost>> ListPublishedPagedAsync(int limit, string? afterCursor, CancellationToken cancellationToken)
    {
        LastPublishedLimit = limit;
        LastAfterCursor = afterCursor;
        return ValueTask.FromResult(ListPublishedResult);
    }

    public ValueTask<Result<BlogPost>> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        LastSlugLookup = slug;
        return ValueTask.FromResult(PublishedBySlugResult);
    }

    public ValueTask<IReadOnlyCollection<BlogPost>> ListAllAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(AllPosts);
    }
}

internal sealed class RecordingBlogSettingsStore : IBlogSettingsStore
{
    public BlogModuleSettings CurrentSettings { get; set; } = BlogTestData.CreateSettings();

    public Result<BlogModuleSettings> UpdateResult { get; set; } = Result<BlogModuleSettings>.Success(BlogTestData.CreateSettings());

    public int UpdateCalls { get; private set; }

    public int LastExpectedVersion { get; private set; }

    public string? LastOperatorSummary { get; private set; }

    public int LastPreviewLimit { get; private set; }

    public string? LastActorId { get; private set; }

    public Instant LastUpdatedUtc { get; private set; }

    public ValueTask<BlogModuleSettings> GetAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(CurrentSettings);
    }

    public ValueTask<Result<BlogModuleSettings>> UpdateAsync(
        int expectedVersion,
        string operatorSummary,
        int previewLimit,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        UpdateCalls += 1;
        LastExpectedVersion = expectedVersion;
        LastOperatorSummary = operatorSummary;
        LastPreviewLimit = previewLimit;
        LastActorId = actorId;
        LastUpdatedUtc = now;
        return ValueTask.FromResult(UpdateResult);
    }
}

internal sealed class RecordingBlogTaxonomyStore : IBlogTaxonomyStore
{
    public IReadOnlyCollection<BlogCategory> Categories { get; set; } = [];

    public IReadOnlyCollection<BlogTag> Tags { get; set; } = [];

    public Result<BlogCategory> GetCategoryBySlugResult { get; set; } = Result<BlogCategory>.Failure(BlogTaxonomyErrors.CategoryNotFound(string.Empty));

    public Result<BlogTag> GetTagBySlugResult { get; set; } = Result<BlogTag>.Failure(BlogTaxonomyErrors.TagNotFound(string.Empty));

    public Result<BlogCategory> CreateCategoryResult { get; set; } = Result<BlogCategory>.Failure(BlogTaxonomyErrors.CategoryNotFound(string.Empty));

    public Result<BlogCategory> UpdateCategoryResult { get; set; } = Result<BlogCategory>.Failure(BlogTaxonomyErrors.CategoryNotFound(string.Empty));

    public Result<BlogTag> CreateTagResult { get; set; } = Result<BlogTag>.Failure(BlogTaxonomyErrors.TagNotFound(string.Empty));

    public Result<BlogTag> UpdateTagResult { get; set; } = Result<BlogTag>.Failure(BlogTaxonomyErrors.TagNotFound(string.Empty));

    public string? LastCategorySlug { get; private set; }

    public string? LastCategoryName { get; private set; }

    public string? LastCategoryDescription { get; private set; }

    public int LastCategoryExpectedVersion { get; private set; }

    public string? LastCategoryActorId { get; private set; }

    public Instant LastCategoryNow { get; private set; }

    public string? LastTagSlug { get; private set; }

    public string? LastTagDisplayName { get; private set; }

    public string? LastTagDescription { get; private set; }

    public int LastTagExpectedVersion { get; private set; }

    public string? LastTagActorId { get; private set; }

    public Instant LastTagNow { get; private set; }

    public ValueTask<IReadOnlyCollection<BlogCategory>> ListCategoriesAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(Categories);
    }

    public ValueTask<IReadOnlyCollection<BlogTag>> ListTagsAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(Tags);
    }

    public ValueTask<Result<BlogCategory>> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        LastCategorySlug = slug;
        return ValueTask.FromResult(GetCategoryBySlugResult);
    }

    public ValueTask<Result<BlogTag>> GetTagBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        LastTagSlug = slug;
        return ValueTask.FromResult(GetTagBySlugResult);
    }

    public ValueTask<Result<BlogCategory>> CreateCategoryAsync(
        string slug,
        string name,
        string? description,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastCategorySlug = slug;
        LastCategoryName = name;
        LastCategoryDescription = description;
        LastCategoryActorId = actorId;
        LastCategoryNow = now;
        return ValueTask.FromResult(CreateCategoryResult);
    }

    public ValueTask<Result<BlogCategory>> UpdateCategoryAsync(
        string slug,
        int expectedVersion,
        string name,
        string? description,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastCategorySlug = slug;
        LastCategoryExpectedVersion = expectedVersion;
        LastCategoryName = name;
        LastCategoryDescription = description;
        LastCategoryActorId = actorId;
        LastCategoryNow = now;
        return ValueTask.FromResult(UpdateCategoryResult);
    }

    public ValueTask<Result<BlogTag>> CreateTagAsync(
        string slug,
        string displayName,
        string? description,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastTagSlug = slug;
        LastTagDisplayName = displayName;
        LastTagDescription = description;
        LastTagActorId = actorId;
        LastTagNow = now;
        return ValueTask.FromResult(CreateTagResult);
    }

    public ValueTask<Result<BlogTag>> UpdateTagAsync(
        string slug,
        int expectedVersion,
        string displayName,
        string? description,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        LastTagSlug = slug;
        LastTagExpectedVersion = expectedVersion;
        LastTagDisplayName = displayName;
        LastTagDescription = description;
        LastTagActorId = actorId;
        LastTagNow = now;
        return ValueTask.FromResult(UpdateTagResult);
    }
}

internal sealed class RecordingBlogOutboxPublisher : IIntegrationEventOutboxPublisher
{
    public List<IntegrationEventOutboxPublishRequest> Requests { get; } = [];

    public ValueTask PublishAsync(IntegrationEventOutboxPublishRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return ValueTask.CompletedTask;
    }
}

internal sealed class StubBlogIdentityTimeZoneReader : IBlogIdentityTimeZoneReader
{
    public List<string> ActorIds { get; } = [];

    public Result<string> NextResult { get; set; } = Result<string>.Success("Etc/UTC");

    public ValueTask<Result<string>> GetRequiredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken)
    {
        ActorIds.Add(actorId);
        return ValueTask.FromResult(NextResult);
    }
}
