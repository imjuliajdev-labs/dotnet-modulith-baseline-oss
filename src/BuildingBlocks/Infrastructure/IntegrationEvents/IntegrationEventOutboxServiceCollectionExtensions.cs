using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

public static class IntegrationEventOutboxServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresIntegrationEventOutbox(
        this IServiceCollection services,
        string moduleKey,
        string schemaName,
        string tableName = PostgresModuleIntegrationEventOutboxStore.DefaultTableName,
        string connectionStringName = SharedRuntimePersistenceDefaults.ConnectionStringName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        PostgresModuleIntegrationEventOutboxStore? registration = null;
        var sync = new object();

        PostgresModuleIntegrationEventOutboxStore Resolve(IServiceProvider serviceProvider)
        {
            lock (sync)
            {
                registration ??= new PostgresModuleIntegrationEventOutboxStore(
                    serviceProvider.GetRequiredService<IPostgresDataSourceResolver>(),
                    moduleKey,
                    schemaName,
                    tableName,
                    connectionStringName);

                return registration;
            }
        }

        services.AddSingleton<IIntegrationEventOutboxStore>(Resolve);
        services.AddSingleton<IDatabaseMigration>(Resolve);
        services.AddSingleton<ICommandTransactionParticipant>(new ModuleCommandTransactionParticipant(moduleKey));

        return services;
    }

    private sealed record ModuleCommandTransactionParticipant(string ModuleKey) : ICommandTransactionParticipant;
}
