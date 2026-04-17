using BuildingBlocks.Application.Modules;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;

namespace BuildingBlocks.Infrastructure.Modules;

public static class ModuleEndpointBulkheadServiceCollectionExtensions
{
    public const int DefaultPermitLimit = 32;
    public const int DefaultQueueLimit = 0;

    public static IServiceCollection AddModuleEndpointBulkheads(
        this IServiceCollection services,
        int permitLimit = DefaultPermitLimit,
        int queueLimit = DefaultQueueLimit)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (permitLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(permitLimit), permitLimit, "Permit limit must be greater than zero.");
        }

        if (queueLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueLimit), queueLimit, "Queue limit cannot be negative.");
        }

        services.AddOptions<RateLimiterOptions>()
            .Configure<IEnumerable<IModule>>((options, modules) =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                foreach (var module in modules
                    .GroupBy(static entry => entry.Key, StringComparer.Ordinal)
                    .Select(static group => group.First()))
                {
                    options.AddConcurrencyLimiter(ModuleEndpointBulkheadPolicyNames.For(module.Key), limiter =>
                    {
                        limiter.PermitLimit = permitLimit;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                        limiter.QueueLimit = queueLimit;
                    });
                }
            });

        return services;
    }
}

public static class ModuleEndpointBulkheadPolicyNames
{
    public static string For(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        return $"module-endpoint-bulkhead:{moduleKey}";
    }
}
