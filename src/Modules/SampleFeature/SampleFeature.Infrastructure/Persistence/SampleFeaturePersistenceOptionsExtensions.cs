using Microsoft.EntityFrameworkCore;

namespace SampleFeature.Infrastructure.Persistence;

internal static class SampleFeaturePersistenceOptionsExtensions
{
    public static DbContextOptionsBuilder UseSampleFeaturePersistence(
        this DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return options.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable(
                    SampleFeaturePersistenceDefaults.MigrationsHistoryTableName,
                    SampleFeaturePersistenceDefaults.SchemaName);
            });
    }
}
