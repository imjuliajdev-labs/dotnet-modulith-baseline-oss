using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

internal static class IdentityPersistenceOptionsExtensions
{
    public static DbContextOptionsBuilder UseIdentityPersistence(this DbContextOptionsBuilder optionsBuilder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", IdentityPersistenceDefaults.SchemaName));

        return optionsBuilder;
    }
}
