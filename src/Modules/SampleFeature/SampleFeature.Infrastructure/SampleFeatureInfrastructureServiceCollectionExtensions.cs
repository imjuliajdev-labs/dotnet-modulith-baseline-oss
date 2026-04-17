using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SampleFeature.Application.Authorization;
using SampleFeature.Application.Publishing;
using SampleFeature.Application.Scheduling;
using SampleFeature.Infrastructure.Outbox;
using SampleFeature.Infrastructure.Persistence;
using SampleFeature.Infrastructure.Workers;

namespace SampleFeature.Infrastructure;

public static class SampleFeatureInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSampleFeatureInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSampleFeatureOutbox();
        services.AddDatabaseMigrationSupport();

        services.AddOptions<ScheduledSampleAnnouncementWorkerOptions>()
            .Configure<IConfiguration>(static (options, configuration) =>
            {
                configuration.GetSection(ScheduledSampleAnnouncementWorkerOptions.SectionName).Bind(options);
            })
            .Validate(
                static options => options.PollIntervalMilliseconds > 0,
                "SampleFeature scheduling worker configuration requires a positive poll interval.")
            .Validate(
                static options => options.BatchSize > 0,
                "SampleFeature scheduling worker configuration requires a positive batch size.")
            .ValidateOnStart();

        services.AddDbContextFactory<SampleFeaturePersistenceDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(SampleFeaturePersistenceDefaults.ConnectionStringName);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSampleFeaturePersistence(connectionString);
            }
        });

        services.TryAddSingleton<ISampleAnnouncementPublisher, SampleAnnouncementPublisher>();
        services.TryAddSingleton<ISampleAnnouncementWriter, PostgresSampleAnnouncementStore>();
        services.TryAddSingleton<IScheduledSampleAnnouncementStore, PostgresScheduledSampleAnnouncementStore>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDatabaseMigration, SampleFeatureDatabaseMigration>());
        services.AddSingleton<ICommandTransactionParticipant>(new SampleFeatureCommandTransactionParticipant(SampleFeatureModuleInfo.ModuleKey));

        services.TryAddSingleton(static provider => provider.GetRequiredService<IOptions<ScheduledSampleAnnouncementWorkerOptions>>().Value);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ScheduledSampleAnnouncementWorker>());

        return services;
    }
}

internal sealed record SampleFeatureCommandTransactionParticipant(string ModuleKey) : ICommandTransactionParticipant;
