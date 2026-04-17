using Microsoft.Extensions.Hosting;

namespace Platform.Infrastructure.Persistence;

internal sealed class PlatformOperationalStoreHostedService : IHostedService
{
    private readonly PostgresPlatformOperationalStore _store;

    public PlatformOperationalStoreHostedService(PostgresPlatformOperationalStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken);
        await _store.RecoverIncompleteTransitionsAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
