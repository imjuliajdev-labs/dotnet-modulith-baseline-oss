using Blog.PublicContracts.Queries;
using Blog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Queries;

internal sealed class BlogPublishedPostQueryService : IBlogPublishedPostQueryService
{
    private readonly IDbContextFactory<BlogPersistenceDbContext> _dbContextFactory;

    public BlogPublishedPostQueryService(IDbContextFactory<BlogPersistenceDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async ValueTask<IReadOnlyCollection<BlogReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var boundedLimit = limit <= 0 ? 10 : limit;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Posts
            .AsNoTracking()
            .Where(record => record.Status == Blog.Domain.Posts.BlogPostStatus.Published)
            .OrderByDescending(record => record.Featured)
            .ThenByDescending(record => record.PublishedUtc)
            .ThenBy(record => record.Title)
            .Take(boundedLimit)
            .Select(record => new BlogReadModel(
                record.PostId,
                record.Slug,
                record.Title,
                record.Summary,
                (record.PublishedUtc ?? record.UpdatedUtc).ToDateTimeOffset()))
            .ToArrayAsync(cancellationToken);
    }
}
