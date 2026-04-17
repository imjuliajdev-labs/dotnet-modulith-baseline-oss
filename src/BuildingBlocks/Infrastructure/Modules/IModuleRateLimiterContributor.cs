using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Modules;

public interface IModuleRateLimiterContributor
{
    void RegisterRateLimiterPolicies(IServiceCollection services);
}
