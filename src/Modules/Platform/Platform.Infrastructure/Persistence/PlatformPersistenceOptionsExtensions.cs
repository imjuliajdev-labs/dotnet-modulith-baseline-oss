using Microsoft.EntityFrameworkCore;

namespace Platform.Infrastructure.Persistence;

internal static class PlatformPersistenceOptionsExtensions
{
    public static DbContextOptionsBuilder UsePlatformPersistence(
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
                    PlatformPersistenceDefaults.MigrationsHistoryTableName,
                    PlatformPersistenceDefaults.SchemaName);
            });
    }
}
