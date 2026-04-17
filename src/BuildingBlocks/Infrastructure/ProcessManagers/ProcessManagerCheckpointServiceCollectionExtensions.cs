using BuildingBlocks.Application.ProcessManagers;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.ProcessManagers;

public static class ProcessManagerCheckpointServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresProcessManagerCheckpoints(
        this IServiceCollection services,
        string moduleKey,
        string schemaName,
        string tableName = PostgresProcessManagerCheckpointStore.DefaultTableName,
        string connectionStringName = SharedRuntimePersistenceDefaults.ConnectionStringName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        PostgresProcessManagerCheckpointStore? registration = null;
        var sync = new object();

        PostgresProcessManagerCheckpointStore Resolve(IServiceProvider serviceProvider)
        {
            lock (sync)
            {
                registration ??= new PostgresProcessManagerCheckpointStore(
                    serviceProvider.GetRequiredService<IPostgresDataSourceResolver>(),
                    moduleKey,
                    schemaName,
                    tableName,
                    connectionStringName);

                return registration;
            }
        }

        services.AddSingleton<IProcessManagerCheckpointStore>(Resolve);
        services.AddSingleton<IDatabaseMigration>(Resolve);

        return services;
    }
}
