namespace BuildingBlocks.Application.Modules;

public sealed class NoOpModuleWorkLeaseManager : IModuleWorkLeaseManager
{
    public ValueTask<IAsyncDisposable> AcquireAsync(string moduleKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        return ValueTask.FromResult<IAsyncDisposable>(NoOpLease.Instance);
    }

    private sealed class NoOpLease : IAsyncDisposable
    {
        public static NoOpLease Instance { get; } = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
