using Blog.Application.Taxonomy;
using Blog.Domain.Taxonomy;
using BuildingBlocks.Application.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Blog.Infrastructure.Persistence;

internal sealed class BlogTaxonomyStore : IBlogTaxonomyStore
{
    private readonly IDbContextFactory<BlogPersistenceDbContext> _dbContextFactory;

    public BlogTaxonomyStore(IDbContextFactory<BlogPersistenceDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async ValueTask<IReadOnlyCollection<BlogCategory>> ListCategoriesAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var categories = await dbContext.Categories
            .AsNoTracking()
            .OrderBy(static category => category.Name)
            .ThenBy(static category => category.Slug)
            .ToArrayAsync(cancellationToken);

        return categories.Select(Map).ToArray();
    }

    public async ValueTask<IReadOnlyCollection<BlogTag>> ListTagsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tags = await dbContext.Tags
            .AsNoTracking()
            .OrderBy(static tag => tag.DisplayName)
            .ThenBy(static tag => tag.Slug)
            .ToArrayAsync(cancellationToken);

        return tags.Select(Map).ToArray();
    }

    public async ValueTask<Result<BlogCategory>> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var category = await dbContext.Categories
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Slug == slug, cancellationToken);

        return category is null
            ? Result<BlogCategory>.Failure(BlogTaxonomyErrors.CategoryNotFound(slug))
            : Result<BlogCategory>.Success(Map(category));
    }

    public async ValueTask<Result<BlogTag>> GetTagBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tag = await dbContext.Tags
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Slug == slug, cancellationToken);

        return tag is null
            ? Result<BlogTag>.Failure(BlogTaxonomyErrors.TagNotFound(slug))
            : Result<BlogTag>.Success(Map(tag));
    }

    public async ValueTask<Result<BlogCategory>> CreateCategoryAsync(
        string slug,
        string name,
        string? description,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var record = new BlogCategoryRecord
        {
            Slug = slug,
            Name = name,
            Description = description,
            Version = 1,
            CreatedUtc = now,
            UpdatedUtc = now,
            UpdatedByActorId = actorId,
        };

        dbContext.Categories.Add(record);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<BlogCategory>.Success(Map(record));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<BlogCategory>.Failure(BlogTaxonomyErrors.CategorySlugAlreadyExists(slug));
        }
    }

    public async ValueTask<Result<BlogCategory>> UpdateCategoryAsync(
        string slug,
        int expectedVersion,
        string name,
        string? description,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await dbContext.Categories
            .SingleOrDefaultAsync(category => category.Slug == slug, cancellationToken);

        if (record is null)
        {
            return Result<BlogCategory>.Failure(BlogTaxonomyErrors.CategoryNotFound(slug));
        }

        if (record.Version != expectedVersion)
        {
            return Result<BlogCategory>.Failure(BlogTaxonomyErrors.CategoryVersionConflict(slug));
        }

        record.Name = name;
        record.Description = description;
        record.Version += 1;
        record.UpdatedUtc = now;
        record.UpdatedByActorId = actorId;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<BlogCategory>.Success(Map(record));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<BlogCategory>.Failure(BlogTaxonomyErrors.CategoryVersionConflict(slug));
        }
    }

    public async ValueTask<Result<BlogTag>> CreateTagAsync(
        string slug,
        string displayName,
        string? description,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var record = new BlogTagRecord
        {
            Slug = slug,
            DisplayName = displayName,
            Description = description,
            Version = 1,
            CreatedUtc = now,
            UpdatedUtc = now,
            UpdatedByActorId = actorId,
        };

        dbContext.Tags.Add(record);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<BlogTag>.Success(Map(record));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Result<BlogTag>.Failure(BlogTaxonomyErrors.TagSlugAlreadyExists(slug));
        }
    }

    public async ValueTask<Result<BlogTag>> UpdateTagAsync(
        string slug,
        int expectedVersion,
        string displayName,
        string? description,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await dbContext.Tags
            .SingleOrDefaultAsync(tag => tag.Slug == slug, cancellationToken);

        if (record is null)
        {
            return Result<BlogTag>.Failure(BlogTaxonomyErrors.TagNotFound(slug));
        }

        if (record.Version != expectedVersion)
        {
            return Result<BlogTag>.Failure(BlogTaxonomyErrors.TagVersionConflict(slug));
        }

        record.DisplayName = displayName;
        record.Description = description;
        record.Version += 1;
        record.UpdatedUtc = now;
        record.UpdatedByActorId = actorId;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<BlogTag>.Success(Map(record));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<BlogTag>.Failure(BlogTaxonomyErrors.TagVersionConflict(slug));
        }
    }

    private static BlogCategory Map(BlogCategoryRecord record)
    {
        return new BlogCategory(
            record.Slug,
            record.Name,
            record.Description,
            record.Version,
            record.CreatedUtc,
            record.UpdatedUtc,
            record.UpdatedByActorId);
    }

    private static BlogTag Map(BlogTagRecord record)
    {
        return new BlogTag(
            record.Slug,
            record.DisplayName,
            record.Description,
            record.Version,
            record.CreatedUtc,
            record.UpdatedUtc,
            record.UpdatedByActorId);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
