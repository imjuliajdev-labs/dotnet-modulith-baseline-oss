using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.ProblemDetails;
using BuildingBlocks.Application.ProcessManagers;
using BuildingBlocks.Domain.Time;
using BuildingBlocks.Infrastructure.Authorization;
using BuildingBlocks.Infrastructure.Dispatching;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using BuildingBlocks.Infrastructure.Modules;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.ProblemDetails;
using BuildingBlocks.Infrastructure.ProcessManagers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using BuildingBlocks.Infrastructure.Realtime;
using BuildingBlocks.Infrastructure.Serialization;
using BuildingBlocks.Infrastructure.Telemetry;
using BuildingBlocks.Infrastructure.Time;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBuildingBlocksInfrastructureDefaults(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IConfiguration>(static _ => new ConfigurationBuilder().Build());
        services.AddDatabaseMigrationSupport();
        services.TryAddSingleton<IPostgresDataSourceResolver, ConfiguredPostgresDataSourceResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SharedRuntimePersistenceConfigurationValidationHostedService>());
        services.AddDatabaseMigrationReadinessValidation();
        services.AddHttpContextAccessor();
        services.AddAuthorization();
        services.AddProblemDetails();
        services.AddDataProtection()
            .SetApplicationName(SharedRuntimePersistenceDefaults.DataProtectionApplicationName);
        services.ConfigureHttpJsonOptions(static options => StarterJsonSerializerOptions.Configure(options.SerializerOptions));
        services.AddSignalR().AddJsonProtocol(static options => StarterJsonSerializerOptions.Configure(options.PayloadSerializerOptions));
        services.AddSingleton<IProblemDetailsMapper, ProblemDetailsMapper>();
        services.AddSingleton<IExceptionToErrorMapper, DefaultExceptionToErrorMapper>();
        services.AddSingleton<ProblemDetailsHttpWriter>();
        services.AddSingleton<ResultHttpMapper>();
        services.TryAddSingleton<IBrowserRealtimeNotifier, SignalRBrowserRealtimeNotifier>();
        services.TryAddSingleton<IClock, SystemClockAdapter>();
        services.AddSingleton<IRequestTelemetrySessionFactory, ActivityRequestTelemetrySessionFactory>();
        services.AddOptions<CommandIdempotencyRetentionOptions>()
            .BindConfiguration(CommandIdempotencyRetentionOptions.SectionName)
            .Validate(
                static options => options.RunningEntryTtl > TimeSpan.Zero,
                "Command idempotency requires a positive running-entry retention window.")
            .Validate(
                static options => options.CompletedEntryTtl > TimeSpan.Zero,
                "Command idempotency requires a positive completed-entry retention window.")
            .Validate(
                static options => options.AbandonedEntryTtl > TimeSpan.Zero,
                "Command idempotency requires a positive abandoned-entry retention window.")
            .Validate(
                static options => options.CleanupInterval > TimeSpan.Zero,
                "Command idempotency requires a positive cleanup interval.")
            .ValidateOnStart();
        services.TryAddSingleton(static provider => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommandIdempotencyRetentionOptions>>().Value);
        services.TryAddSingleton(new IntegrationEventOutboxProcessingOptions());
        services.TryAddSingleton<IIntegrationEventOutboxPublisher, IntegrationEventOutboxPublisher>();
        services.TryAddSingleton<IIntegrationEventOutboxDispatcher, IntegrationEventOutboxDispatcher>();
        services.TryAddSingleton<ICommandIdempotencyRequestHasher, JsonCommandIdempotencyRequestHasher>();
        services.TryAddSingleton<ICommandIdempotencyStore>(static serviceProvider =>
            new PostgresCommandIdempotencyStore(
                serviceProvider.GetRequiredService<IPostgresDataSourceResolver>(),
                serviceProvider.GetRequiredService<CommandIdempotencyRetentionOptions>()));
        // Integration event inbox and process-manager checkpoint stores are registered per consuming module
        // (see IntegrationEventInboxServiceCollectionExtensions.AddPostgresIntegrationEventInbox and
        // ProcessManagerCheckpointServiceCollectionExtensions.AddPostgresProcessManagerCheckpoints). The
        // dispatcher resolves the correct store for each integration event by the consuming module's key.
        services.TryAddSingleton<IXmlRepository>(static serviceProvider =>
            new PostgresDataProtectionKeyRepository(
                serviceProvider.GetRequiredService<IPostgresDataSourceResolver>().GetRequiredDataSource(SharedRuntimePersistenceDefaults.ConnectionStringName)));
        services.AddOptions<KeyManagementOptions>()
            .Configure<IXmlRepository>(static (options, repository) => options.XmlRepository = repository);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDatabaseMigration, SharedRuntimePersistenceDatabaseMigration>());
        services.TryAddSingleton<IModuleStateReader, DescriptorBackedModuleStateReader>();
        services.TryAddSingleton<IModuleStateGuard, ModuleStateGuard>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentActorAccessor, HttpContextCurrentActorAccessor>());
        services.Replace(ServiceDescriptor.Scoped<ICommandTransactionScopeFactory, AmbientTransactionScopeCommandTransactionScopeFactory>());
        services.AddSingleton(static serviceProvider =>
        {
            var resolver = serviceProvider.GetRequiredService<IPostgresDataSourceResolver>();
            var connectionString = resolver.GetConnectionString(SharedRuntimePersistenceDefaults.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return new IntegrationEventOutboxSignalHolder(Signal: null);
            }

            return new IntegrationEventOutboxSignalHolder(new IntegrationEventOutboxSignal(
                resolver.GetRequiredDataSource(SharedRuntimePersistenceDefaults.ConnectionStringName),
                serviceProvider.GetRequiredService<ILogger<IntegrationEventOutboxSignal>>()));
        });
        services.TryAddSingleton<IntegrationEventOutboxSignalStatus>();
        // OutboxMetrics owns the `outbox.signal.degraded` ObservableGauge; it must resolve so the gauge becomes active
        // and the meter inventory is exported. Other outbox counters on the same meter are published through the same
        // instance.
        services.TryAddSingleton<OutboxMetrics>();
        services.AddHealthChecks()
            .AddCheck<OutboxSignalHealthCheck>(OutboxSignalHealthCheck.Name, tags: ["ready"]);
        services.TryAddSingleton<OutboxSignalHealthCheck>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, IntegrationEventOutboxHostedService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CommandIdempotencyRetentionHostedService>());
        services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

        return services;
    }
}
