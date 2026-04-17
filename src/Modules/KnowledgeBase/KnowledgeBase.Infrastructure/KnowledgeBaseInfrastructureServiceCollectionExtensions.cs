using BuildingBlocks.Infrastructure.Persistence;
using KnowledgeBase.Application.Authorization;
using KnowledgeBase.Application.Entries;
using KnowledgeBase.Application.Settings;
using KnowledgeBase.Infrastructure.Configuration;
using KnowledgeBase.Infrastructure.Outbox;
using KnowledgeBase.Infrastructure.Persistence;
using KnowledgeBase.Infrastructure.Queries;
using KnowledgeBase.PublicContracts.Queries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Infrastructure;

public static class KnowledgeBaseInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeBaseInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<KnowledgeBaseInfrastructureOptions>()
            .Configure<IConfiguration>(static (options, configuration) =>
            {
                configuration.GetSection(KnowledgeBaseInfrastructureOptions.SectionName).Bind(options);
            })
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.Settings.PublicExperienceTitle),
                "KnowledgeBase settings require a public experience title.")
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.Settings.PublicExperienceBlurb),
                "KnowledgeBase settings require a public experience blurb.")
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.Settings.SearchPlaceholder),
                "KnowledgeBase settings require a search placeholder.")
            .Validate(
                static options => options.Settings.ManagementPreviewLimit >= KnowledgeBaseModuleSettingsDefaults.MinManagementPreviewLimit
                    && options.Settings.ManagementPreviewLimit <= KnowledgeBaseModuleSettingsDefaults.MaxManagementPreviewLimit,
                $"KnowledgeBase settings preview limit must be between {KnowledgeBaseModuleSettingsDefaults.MinManagementPreviewLimit} and {KnowledgeBaseModuleSettingsDefaults.MaxManagementPreviewLimit}.")
            .ValidateOnStart();

        services.TryAddSingleton<IKnowledgeBaseStore>(static serviceProvider =>
        {
            var dataSourceResolver = serviceProvider.GetRequiredService<IPostgresDataSourceResolver>();
            var clock = serviceProvider.GetRequiredService<BuildingBlocks.Domain.Time.IClock>();
            var options = serviceProvider.GetRequiredService<IOptions<KnowledgeBaseInfrastructureOptions>>().Value;
            return new PostgresKnowledgeBaseStore(dataSourceResolver, clock, options);
        });
        services.TryAddSingleton<IKnowledgeBaseSettingsStore>(static provider =>
            (IKnowledgeBaseSettingsStore)provider.GetRequiredService<IKnowledgeBaseStore>());
        services.TryAddSingleton<IKnowledgeBasePublishedEntryQueryService, KnowledgeBasePublishedEntryQueryService>();
        services.AddSingleton<IDatabaseMigration>(static provider => (IDatabaseMigration)provider.GetRequiredService<IKnowledgeBaseStore>());
        services.AddKnowledgeBaseOutbox();

        return services;
    }
}
