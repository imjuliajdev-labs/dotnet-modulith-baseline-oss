using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Platform.Infrastructure.Persistence;

internal sealed class PlatformDatabaseMigration : IDatabaseMigration
{
    private readonly IConfiguration _configuration;
    private readonly IDbContextFactory<PlatformPersistenceDbContext> _dbContextFactory;

    public PlatformDatabaseMigration(
        IConfiguration configuration,
        IDbContextFactory<PlatformPersistenceDbContext> dbContextFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public string Name => PlatformPersistenceDefaults.SchemaName;

    public string? ConnectionString => _configuration.GetConnectionString(PlatformPersistenceDefaults.ConnectionStringName);

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
