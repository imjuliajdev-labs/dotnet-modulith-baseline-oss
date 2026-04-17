using Admin.Application.Authorization;
using Admin.Application.Consumers;
using Admin.Application.ProcessManagers;
using Admin.Application.Queries;
using Admin.Application.Realtime;
using Admin.Infrastructure.Configuration;
using Admin.Infrastructure.Inbox;
using Admin.Infrastructure.ProcessManagers;
using Admin.Infrastructure.Realtime;
using Admin.Infrastructure.Workers;
using Admin.PublicContracts.Queries;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.ProcessManagers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Admin.Infrastructure;

public static class AdminInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAdminInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AdminInfrastructureOptions>()
            .Configure<IConfiguration>(static (options, configuration) =>
            {
                configuration.GetSection(AdminInfrastructureOptions.SectionName).Bind(options);
            })
            .Validate(
                static options => options.ProjectionRecoveryWorker.BatchSize > 0,
                "Admin projection recovery worker batch size must be positive.")
            .Validate(
                static options => options.ProjectionRecoveryWorker.PollIntervalMilliseconds > 0,
                "Admin projection recovery worker poll interval must be positive.")
            .Validate(
                static options => options.ProjectionRecoveryWorker.StaleAfterMilliseconds >= 0,
                "Admin projection recovery worker stale-after threshold must be non-negative.")
            .ValidateOnStart();

        services.AddPostgresIntegrationEventInbox(
            AdminModuleInfo.ModuleKey,
            AdminPersistenceDefaults.SchemaName);

        services.AddPostgresProcessManagerCheckpoints(
            AdminModuleInfo.ModuleKey,
            AdminPersistenceDefaults.SchemaName);

        services.AddSingleton<AdminAnnouncementInboxWriter>();
        services.TryAddSingleton<IAdminAnnouncementInbox>(static provider => provider.GetRequiredService<AdminAnnouncementInboxWriter>());
        services.TryAddSingleton<IAdminAnnouncementQueryService, AdminAnnouncementReader>();
        services.AddSingleton<IAdminAnnouncementProjectionProcessManagerStore, AdminAnnouncementProjectionProcessManagerStore>();
        services.AddSingleton<IAdminAnnouncementRealtimeNotifier, AdminAnnouncementRealtimeNotifier>();
        services.AddSingleton<IAdminAnnouncementProjectionProcessManager, AdminAnnouncementProjectionProcessManager>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AdminAnnouncementProjectionRecoveryWorker>());
        services.AddSingleton<IDatabaseMigration>(static provider => provider.GetRequiredService<AdminAnnouncementInboxWriter>());

        return services;
    }
}
