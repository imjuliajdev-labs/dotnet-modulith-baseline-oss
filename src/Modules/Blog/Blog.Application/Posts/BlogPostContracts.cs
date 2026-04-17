using Blog.Application.Authorization;
using Blog.Application.Scheduling;
using Blog.Domain.Posts;
using Blog.PublicContracts.Events;
using BuildingBlocks.Application;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace Blog.Application.Posts;

public sealed record BlogPostCollection(IReadOnlyCollection<BlogPost> Posts);

public interface IBlogPostReadQueries
{
    ValueTask<CursorPagedResult<BlogPost>> ListPublishedPagedAsync(int limit, string? afterCursor, CancellationToken cancellationToken);

    ValueTask<Result<BlogPost>> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<BlogPost>> ListAllAsync(CancellationToken cancellationToken);
}

public interface IBlogPostStore
{
    ValueTask<Result<BlogPost>> GetByIdAsync(Guid postId, CancellationToken cancellationToken);

    ValueTask<Result<BlogPost>> CreateAsync(
        BlogPostDraft draft,
        bool featured,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);

    ValueTask<Result<BlogPost>> UpdateAsync(
        Guid postId,
        int expectedVersion,
        BlogPostDraft draft,
        bool featured,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);

    ValueTask<Result<BlogPost>> SetStatusAsync(
        Guid postId,
        int expectedVersion,
        BlogPostStatus status,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);

    ValueTask<Result<BlogPost>> PublishAsync(
        Guid postId,
        int expectedVersion,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);

    ValueTask<Result<BlogPost>> SchedulePublicationAsync(
        Guid postId,
        int expectedVersion,
        BlogPublicationSchedule publicationSchedule,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<ProcessedBlogPostTransition>> ProcessDueScheduledTransitionsAsync(
        NodaTime.Instant now,
        int batchSize,
        CancellationToken cancellationToken);

    ValueTask<Result> RegisterPublishedViewAsync(string slug, CancellationToken cancellationToken);
}

public static class BlogPostErrors
{
    public static Error BodyRequired()
    {
        return new Error(
            "blog.body_required",
            "Blog post body is required.",
            ErrorKind.Validation);
    }

    public static Error ConcurrencyConflict(Guid postId)
    {
        return new Error(
            "blog.post_version_conflict",
            $"Blog post '{postId}' was changed by another operation.",
            ErrorKind.Conflict);
    }

    public static Error InvalidCursor()
    {
        return new Error(
            "blog.invalid_cursor",
            "The pagination cursor is malformed and was rejected.",
            ErrorKind.Validation);
    }

    public static Error InvalidStatus(string status)
    {
        return new Error(
            "blog.invalid_status",
            $"Blog post status '{status}' is not supported.",
            ErrorKind.Validation);
    }

    public static Error PostNotFound(Guid postId)
    {
        return new Error(
            "blog.post_not_found",
            $"Blog post '{postId}' was not found.",
            ErrorKind.NotFound);
    }

    public static Error PublishRequiresDedicatedEndpoint()
    {
        return new Error(
            "blog.publish_requires_dedicated_endpoint",
            "Publishing requires the dedicated blog publish endpoint.",
            ErrorKind.Validation);
    }

    public static Error PublishedPostNotFound(string slug)
    {
        return new Error(
            "blog.published_post_not_found",
            $"Published blog post '{slug}' was not found.",
            ErrorKind.NotFound);
    }

    public static Error SlugAlreadyExists(string slug)
    {
        return new Error(
            "blog.slug_already_exists",
            $"Blog slug '{slug}' already exists.",
            ErrorKind.Conflict);
    }

    public static Error SummaryRequired()
    {
        return new Error(
            "blog.summary_required",
            "Blog post summary is required.",
            ErrorKind.Validation);
    }

    public static Error TitleRequired()
    {
        return new Error(
            "blog.title_required",
            "Blog post title is required.",
            ErrorKind.Validation);
    }

    public static Error VersionMustBePositive()
    {
        return new Error(
            "blog.version_required",
            "A positive expected version is required for blog post mutations.",
            ErrorKind.Validation);
    }
}

public sealed record ListPublishedBlogPostsQuery(int? Limit = null, string? After = null) : IQuery<CursorPagedResult<BlogPost>>, IModuleScoped
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;
}

internal sealed class ListPublishedBlogPostsQueryHandler : IQueryHandler<ListPublishedBlogPostsQuery, CursorPagedResult<BlogPost>>
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    private readonly IBlogPostReadQueries _readQueries;

    public ListPublishedBlogPostsQueryHandler(IBlogPostReadQueries readQueries)
    {
        _readQueries = readQueries ?? throw new ArgumentNullException(nameof(readQueries));
    }

    public async Task<Result<CursorPagedResult<BlogPost>>> Handle(ListPublishedBlogPostsQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit is > 0
            ? Math.Min(query.Limit.Value, MaxLimit)
            : DefaultLimit;

        if (!string.IsNullOrEmpty(query.After))
        {
            var decoded = CursorEncoding.Decode(query.After);
            if (decoded.Status != CursorDecodeStatus.Valid
                || !System.DateTimeOffset.TryParse(decoded.SortValue, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _)
                || !System.Guid.TryParse(decoded.Id, out _))
            {
                return Result<CursorPagedResult<BlogPost>>.Failure(BlogPostErrors.InvalidCursor());
            }
        }

        var result = await _readQueries.ListPublishedPagedAsync(limit, query.After, cancellationToken);
        return Result<CursorPagedResult<BlogPost>>.Success(result);
    }
}

public sealed record GetPublishedBlogPostBySlugQuery(string Slug) : IQuery<BlogPost>, IModuleScoped
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;
}

internal sealed class GetPublishedBlogPostBySlugQueryValidator : IRequestValidator<GetPublishedBlogPostBySlugQuery>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(GetPublishedBlogPostBySlugQuery request, CancellationToken cancellationToken)
    {
        var error = BlogPostNormalization.ValidatePublicSlug(request.Slug);
        return Task.FromResult<IReadOnlyList<Error>>(error is null ? Array.Empty<Error>() : [error]);
    }
}

internal sealed class GetPublishedBlogPostBySlugQueryHandler : IQueryHandler<GetPublishedBlogPostBySlugQuery, BlogPost>
{
    private readonly IBlogPostReadQueries _readQueries;

    public GetPublishedBlogPostBySlugQueryHandler(IBlogPostReadQueries readQueries)
    {
        _readQueries = readQueries ?? throw new ArgumentNullException(nameof(readQueries));
    }

    public Task<Result<BlogPost>> Handle(GetPublishedBlogPostBySlugQuery query, CancellationToken cancellationToken)
    {
        return _readQueries.GetPublishedBySlugAsync(query.Slug.Trim(), cancellationToken).AsTask();
    }
}

public sealed record RegisterBlogPostViewCommand(string Slug) : ICommand, IModuleScoped
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;
}

internal sealed class RegisterBlogPostViewCommandValidator : IRequestValidator<RegisterBlogPostViewCommand>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(RegisterBlogPostViewCommand request, CancellationToken cancellationToken)
    {
        var error = BlogPostNormalization.ValidatePublicSlug(request.Slug);
        return Task.FromResult<IReadOnlyList<Error>>(error is null ? Array.Empty<Error>() : [error]);
    }
}

internal sealed class RegisterBlogPostViewCommandHandler : ICommandHandler<RegisterBlogPostViewCommand>
{
    private readonly IBlogPostStore _store;

    public RegisterBlogPostViewCommandHandler(IBlogPostStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<Result> Handle(RegisterBlogPostViewCommand command, CancellationToken cancellationToken)
    {
        return _store.RegisterPublishedViewAsync(command.Slug.Trim(), cancellationToken).AsTask();
    }
}

public sealed record ListBlogPostsForManagementQuery : IQuery<BlogPostCollection>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class ListBlogPostsForManagementQueryHandler : IQueryHandler<ListBlogPostsForManagementQuery, BlogPostCollection>
{
    private readonly IBlogPostReadQueries _readQueries;

    public ListBlogPostsForManagementQueryHandler(IBlogPostReadQueries readQueries)
    {
        _readQueries = readQueries ?? throw new ArgumentNullException(nameof(readQueries));
    }

    public async Task<Result<BlogPostCollection>> Handle(ListBlogPostsForManagementQuery query, CancellationToken cancellationToken)
    {
        var posts = await _readQueries.ListAllAsync(cancellationToken);
        return Result<BlogPostCollection>.Success(new BlogPostCollection(posts));
    }
}

