using BuildingBlocks.Infrastructure.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace SampleFeature.Infrastructure.Outbox;

internal static class SampleFeatureOutboxRegistration
{
    public static IServiceCollection AddSampleFeatureOutbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPostgresIntegrationEventOutbox("sample-feature", "sample_feature");
        return services;
    }
}
