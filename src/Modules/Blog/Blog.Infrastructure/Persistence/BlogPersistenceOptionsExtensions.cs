using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Persistence;

internal static class BlogPersistenceOptionsExtensions
{
    public static DbContextOptionsBuilder UseBlogPersistence(
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
                    BlogPersistenceDefaults.MigrationsHistoryTableName,
                    BlogPersistenceDefaults.SchemaName);
            });
    }
}
