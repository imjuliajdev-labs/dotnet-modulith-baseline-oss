using BuildingBlocks.Infrastructure.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace Blog.Infrastructure.Outbox;

internal static class BlogOutboxRegistration
{
    public static IServiceCollection AddBlogOutbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPostgresIntegrationEventOutbox("blog", "blog");
        return services;
    }
}
