using BuildingBlocks.Infrastructure.IntegrationEvents;
using KnowledgeBase.Application.Authorization;
using KnowledgeBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeBase.Infrastructure.Outbox;

internal static class KnowledgeBaseOutboxRegistration
{
    public static IServiceCollection AddKnowledgeBaseOutbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPostgresIntegrationEventOutbox(
            KnowledgeBaseModuleInfo.ModuleKey,
            KnowledgeBasePersistenceDefaults.SchemaName);
        return services;
    }
}
