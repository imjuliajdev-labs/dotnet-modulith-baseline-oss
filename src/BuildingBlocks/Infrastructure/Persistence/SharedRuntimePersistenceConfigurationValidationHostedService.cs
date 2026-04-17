using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Infrastructure.Persistence;

internal sealed class SharedRuntimePersistenceConfigurationValidationHostedService : IHostedService
{
    private readonly IConfiguration _configuration;

    public SharedRuntimePersistenceConfigurationValidationHostedService(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = SharedRuntimePersistenceDefaults.GetRequiredConnectionString(_configuration);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
