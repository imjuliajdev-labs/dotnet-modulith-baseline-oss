using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

public static class IntegrationEventInboxServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresIntegrationEventInbox(
        this IServiceCollection services,
        string moduleKey,
        string schemaName,
        string tableName = PostgresIntegrationEventInboxStore.DefaultTableName,
        string connectionStringName = SharedRuntimePersistenceDefaults.ConnectionStringName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        PostgresIntegrationEventInboxStore? registration = null;
        var sync = new object();

        PostgresIntegrationEventInboxStore Resolve(IServiceProvider serviceProvider)
        {
            lock (sync)
            {
                registration ??= new PostgresIntegrationEventInboxStore(
                    serviceProvider.GetRequiredService<IPostgresDataSourceResolver>(),
                    moduleKey,
                    schemaName,
                    tableName,
                    connectionStringName);

                return registration;
            }
        }

        services.AddSingleton<IIntegrationEventInboxStore>(Resolve);
        services.AddSingleton<IDatabaseMigration>(Resolve);

        return services;
    }
}
