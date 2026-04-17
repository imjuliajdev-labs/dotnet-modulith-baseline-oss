using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Infrastructure.Persistence;

public interface IDatabaseMigration
{
    string Name { get; }

    string? ConnectionString { get; }

    Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken);

    Task ApplyAsync(CancellationToken cancellationToken);
}

public interface IDatabaseMigrationRunner
{
    Task<IReadOnlyCollection<DatabaseMigrationStatus>> GetStatusAsync(CancellationToken cancellationToken);

    Task EnsureReadyAsync(CancellationToken cancellationToken);

    Task ApplyConfiguredMigrationsAsync(CancellationToken cancellationToken);
}

public sealed record DatabaseMigrationStatus(
    string Name,
    bool IsConfigured,
    IReadOnlyCollection<string> PendingMigrations);

public static class DatabaseMigrationServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseMigrationSupport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IDatabaseMigrationRunner, DatabaseMigrationRunner>();
        return services;
    }

    public static IServiceCollection AddDatabaseMigrationReadinessValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, DatabaseMigrationReadinessHostedService>());

        return services;
    }
}
