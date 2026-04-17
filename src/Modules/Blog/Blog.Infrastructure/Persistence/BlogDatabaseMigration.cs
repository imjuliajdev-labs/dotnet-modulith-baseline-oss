using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Blog.Infrastructure.Persistence;

internal sealed class BlogDatabaseMigration : IDatabaseMigration
{
    private readonly IConfiguration _configuration;
    private readonly IDbContextFactory<BlogPersistenceDbContext> _dbContextFactory;

    public BlogDatabaseMigration(
        IConfiguration configuration,
        IDbContextFactory<BlogPersistenceDbContext> dbContextFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public string Name => BlogPersistenceDefaults.SchemaName;

    public string? ConnectionString => _configuration.GetConnectionString(BlogPersistenceDefaults.ConnectionStringName);

    public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return Array.Empty<string>();
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
