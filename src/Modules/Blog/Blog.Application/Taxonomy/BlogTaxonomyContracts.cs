using Blog.Application.Authorization;
using Blog.Application.Posts;
using Blog.Domain.Taxonomy;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

namespace Blog.Application.Taxonomy;

public sealed record BlogCategoryCollection(IReadOnlyCollection<BlogCategory> Categories);

public sealed record BlogTagCollection(IReadOnlyCollection<BlogTag> Tags);

public interface IBlogTaxonomyStore
{
    ValueTask<IReadOnlyCollection<BlogCategory>> ListCategoriesAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<BlogTag>> ListTagsAsync(CancellationToken cancellationToken);

    ValueTask<Result<BlogCategory>> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken);

    ValueTask<Result<BlogTag>> GetTagBySlugAsync(string slug, CancellationToken cancellationToken);

    ValueTask<Result<BlogCategory>> CreateCategoryAsync(
        string slug,
        string name,
        string? description,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);

    ValueTask<Result<BlogCategory>> UpdateCategoryAsync(
        string slug,
        int expectedVersion,
        string name,
        string? description,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);

    ValueTask<Result<BlogTag>> CreateTagAsync(
        string slug,
        string displayName,
        string? description,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);

    ValueTask<Result<BlogTag>> UpdateTagAsync(
        string slug,
        int expectedVersion,
        string displayName,
        string? description,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);
}

public static class BlogTaxonomyErrors
{
    public static Error CategoryNameRequired()
    {
        return new Error(
            "blog.category_name_required",
            "Blog category name is required.",
            ErrorKind.Validation);
    }

    public static Error CategoryNotFound(string slug)
    {
        return new Error(
            "blog.category_not_found",
            $"Blog category '{slug}' was not found.",
            ErrorKind.NotFound);
    }

    public static Error CategorySlugAlreadyExists(string slug)
    {
        return new Error(
            "blog.category_slug_exists",
            $"Blog category slug '{slug}' already exists.",
            ErrorKind.Conflict);
    }

    public static Error CategoryVersionConflict(string slug)
    {
        return new Error(
            "blog.category_version_conflict",
            $"Blog category '{slug}' was changed by another operation.",
            ErrorKind.Conflict);
    }

    public static Error TagDisplayNameRequired()
    {
        return new Error(
            "blog.tag_display_name_required",
            "Blog tag display name is required.",
            ErrorKind.Validation);
    }

    public static Error TagNotFound(string slug)
    {
        return new Error(
            "blog.tag_not_found",
            $"Blog tag '{slug}' was not found.",
            ErrorKind.NotFound);
    }

    public static Error TagSlugAlreadyExists(string slug)
    {
        return new Error(
            "blog.tag_slug_exists",
            $"Blog tag slug '{slug}' already exists.",
            ErrorKind.Conflict);
    }

    public static Error TagVersionConflict(string slug)
    {
        return new Error(
            "blog.tag_version_conflict",
            $"Blog tag '{slug}' was changed by another operation.",
            ErrorKind.Conflict);
    }
}

public sealed record ListBlogCategoriesQuery : IQuery<BlogCategoryCollection>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class ListBlogCategoriesQueryHandler : IQueryHandler<ListBlogCategoriesQuery, BlogCategoryCollection>
{
    private readonly IBlogTaxonomyStore _store;

    public ListBlogCategoriesQueryHandler(IBlogTaxonomyStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogCategoryCollection>> Handle(ListBlogCategoriesQuery query, CancellationToken cancellationToken)
    {
        var categories = await _store.ListCategoriesAsync(cancellationToken);
        return Result<BlogCategoryCollection>.Success(new BlogCategoryCollection(categories));
    }
}

public sealed record ListBlogTagsQuery : IQuery<BlogTagCollection>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => BlogModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class ListBlogTagsQueryHandler : IQueryHandler<ListBlogTagsQuery, BlogTagCollection>
{
    private readonly IBlogTaxonomyStore _store;

    public ListBlogTagsQueryHandler(IBlogTaxonomyStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<BlogTagCollection>> Handle(ListBlogTagsQuery query, CancellationToken cancellationToken)
    {
        var tags = await _store.ListTagsAsync(cancellationToken);
        return Result<BlogTagCollection>.Success(new BlogTagCollection(tags));
    }
}

internal sealed record NormalizedBlogCategory(string Slug, string Name, string? Description);

internal sealed record NormalizedBlogTag(string Slug, string DisplayName, string? Description);

internal static class BlogTaxonomyNormalization
{
    public static Result<NormalizedBlogCategory> NormalizeCategory(
        string? slug,
        string name,
        string? description,
        bool slugIsExplicit = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<NormalizedBlogCategory>.Failure(BlogTaxonomyErrors.CategoryNameRequired());
        }

        var normalizedName = name.Trim();
        var normalizedSlug = BlogPostNormalization.CreateSlug(slugIsExplicit ? slug ?? string.Empty : string.IsNullOrWhiteSpace(slug) ? normalizedName : slug.Trim());
        var normalizedDescription = NormalizeOptionalText(description);

        return Result<NormalizedBlogCategory>.Success(new NormalizedBlogCategory(normalizedSlug, normalizedName, normalizedDescription));
    }

    public static Result<NormalizedBlogTag> NormalizeTag(
        string? slug,
        string displayName,
        string? description,
        bool slugIsExplicit = false)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result<NormalizedBlogTag>.Failure(BlogTaxonomyErrors.TagDisplayNameRequired());
        }

        var normalizedDisplayName = displayName.Trim();
        var normalizedSlug = BlogPostNormalization.CreateSlug(slugIsExplicit ? slug ?? string.Empty : string.IsNullOrWhiteSpace(slug) ? normalizedDisplayName : slug.Trim());
        var normalizedDescription = NormalizeOptionalText(description);

        return Result<NormalizedBlogTag>.Success(new NormalizedBlogTag(normalizedSlug, normalizedDisplayName, normalizedDescription));
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static Task WriteAuditAsync(
        IAuditEventWriter auditEventWriter,
        IRequestContextAccessor requestContextAccessor,
        string actorId,
        NodaTime.Instant occurredUtc,
        string action,
        string targetType,
        string targetId,
        string outcome,
        CancellationToken cancellationToken)
    {
        return auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: BlogModuleInfo.ModuleKey,
                Action: action,
                TargetType: targetType,
                TargetId: targetId,
                Outcome: outcome,
                OccurredUtc: occurredUtc,
                ActorId: actorId,
                CorrelationId: requestContextAccessor.Current?.CorrelationId),
            cancellationToken);
    }

    internal static Task WriteCategoryAuditAsync(
        IAuditEventWriter auditEventWriter,
        IRequestContextAccessor requestContextAccessor,
        string actorId,
        NodaTime.Instant occurredUtc,
        string action,
        string targetId,
        string outcome,
        CancellationToken cancellationToken)
    {
        return WriteAuditAsync(auditEventWriter, requestContextAccessor, actorId, occurredUtc, action, "blog-category", targetId, outcome, cancellationToken);
    }

    internal static Task WriteTagAuditAsync(
        IAuditEventWriter auditEventWriter,
        IRequestContextAccessor requestContextAccessor,
        string actorId,
        NodaTime.Instant occurredUtc,
        string action,
        string targetId,
        string outcome,
        CancellationToken cancellationToken)
    {
        return WriteAuditAsync(auditEventWriter, requestContextAccessor, actorId, occurredUtc, action, "blog-tag", targetId, outcome, cancellationToken);
    }
}
