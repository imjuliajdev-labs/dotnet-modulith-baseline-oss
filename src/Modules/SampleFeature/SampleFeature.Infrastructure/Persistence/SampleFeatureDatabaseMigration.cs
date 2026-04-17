using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace SampleFeature.Infrastructure.Persistence;

internal sealed class SampleFeatureDatabaseMigration : IDatabaseMigration
{
    private readonly IConfiguration _configuration;
    private readonly IDbContextFactory<SampleFeaturePersistenceDbContext> _dbContextFactory;

    public SampleFeatureDatabaseMigration(
        IConfiguration configuration,
        IDbContextFactory<SampleFeaturePersistenceDbContext> dbContextFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public string Name => SampleFeaturePersistenceDefaults.SchemaName;

    public string? ConnectionString => _configuration.GetConnectionString(SampleFeaturePersistenceDefaults.ConnectionStringName);

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
