using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Platform.Application.Auditing;
using Platform.Application.ModuleState;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Persistence;

namespace Platform.Infrastructure;

public static class PlatformInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<PlatformRateLimiterOptions>()
            .Configure<IConfiguration>(static (options, configuration) =>
            {
                configuration.GetSection(PlatformRateLimiterOptions.SectionName).Bind(options);
            })
            .Validate(
                static options => options.ModuleStateMutationPermitLimit > 0,
                "Platform rate limiter configuration requires a positive module-state mutation permit limit.")
            .ValidateOnStart();

        services.AddDatabaseMigrationSupport();
        services.AddDbContextFactory<PlatformPersistenceDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(PlatformPersistenceDefaults.ConnectionStringName);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UsePlatformPersistence(connectionString);
            }
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDatabaseMigration, PlatformDatabaseMigration>());

        services.AddSingleton<PostgresPlatformOperationalStore>();
        services.AddSingleton<IPlatformModuleStateStore>(static provider => provider.GetRequiredService<PostgresPlatformOperationalStore>());
        services.AddSingleton<IAuditEventWriter>(static provider => provider.GetRequiredService<PostgresPlatformOperationalStore>());
        services.AddSingleton<IPlatformAuditEventReader>(static provider => provider.GetRequiredService<PostgresPlatformOperationalStore>());
        services.Replace(ServiceDescriptor.Singleton<IModuleWorkLeaseManager>(static provider => provider.GetRequiredService<PostgresPlatformOperationalStore>()));
        services.Replace(ServiceDescriptor.Singleton<IModuleStateReader>(static provider => provider.GetRequiredService<IPlatformModuleStateStore>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PlatformOperationalStoreHostedService>());

        return services;
    }
}
