using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

namespace Module.UnitTests;

public sealed class ModuleExecutionGateTests
{
    [Fact]
    public async Task TryEnterAsyncReturnsFailureWithoutAcquiringLeaseWhenTheModuleIsUnavailable()
    {
        var leaseManager = new TrackingModuleWorkLeaseManager();
        var gate = new ModuleExecutionGate(
            new SequenceModuleStateGuard([new Error("module.disabled", "Module 'admin' is disabled.", ErrorKind.ServiceUnavailable)]),
            leaseManager);

        await using var execution = await gate.TryEnterAsync("admin", CancellationToken.None);

        Assert.False(execution.IsEntered);
        Assert.Equal("module.disabled", execution.Failure.Code);
        Assert.Equal(0, leaseManager.AcquireCount);
        Assert.Equal(0, leaseManager.ReleaseCount);
    }

    [Fact]
    public async Task TryEnterAsyncDisposesTheLeaseWhenTheModuleDisablesAfterAcquisition()
    {
        var leaseManager = new TrackingModuleWorkLeaseManager();
        var gate = new ModuleExecutionGate(
            new SequenceModuleStateGuard(
            [
                Error.None,
                new Error("module.disabling", "Module 'admin' is disabling.", ErrorKind.ServiceUnavailable)
            ]),
            leaseManager);

        await using var execution = await gate.TryEnterAsync("admin", CancellationToken.None);

        Assert.False(execution.IsEntered);
        Assert.Equal("module.disabling", execution.Failure.Code);
        Assert.Equal(1, leaseManager.AcquireCount);
        Assert.Equal(1, leaseManager.ReleaseCount);
    }

    [Fact]
    public async Task TryEnterAsyncReturnsAnActiveLeaseWhenTheModuleStaysEnabled()
    {
        var leaseManager = new TrackingModuleWorkLeaseManager();
        var gate = new ModuleExecutionGate(
            new SequenceModuleStateGuard([Error.None, Error.None]),
            leaseManager);

        var execution = await gate.TryEnterAsync("admin", CancellationToken.None);

        Assert.True(execution.IsEntered);
        Assert.Equal(Error.None, execution.Failure);
        Assert.Equal(1, leaseManager.AcquireCount);
        Assert.Equal(0, leaseManager.ReleaseCount);

        await execution.DisposeAsync();

        Assert.Equal(1, leaseManager.ReleaseCount);
    }

    private sealed class SequenceModuleStateGuard : IModuleStateGuard
    {
        private readonly Queue<Error> _results;

        public SequenceModuleStateGuard(IEnumerable<Error> results)
        {
            _results = new Queue<Error>(results ?? throw new ArgumentNullException(nameof(results)));
        }

        public ValueTask<Error> GetFailureOrNoneAsync(string moduleKey, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
            return ValueTask.FromResult(_results.Count == 0 ? Error.None : _results.Dequeue());
        }
    }

    private sealed class TrackingModuleWorkLeaseManager : IModuleWorkLeaseManager
    {
        public int AcquireCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public ValueTask<IAsyncDisposable> AcquireAsync(string moduleKey, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
            AcquireCount++;
            return ValueTask.FromResult<IAsyncDisposable>(new TrackingLease(this));
        }

        private sealed class TrackingLease : IAsyncDisposable
        {
            private readonly TrackingModuleWorkLeaseManager _owner;
            private int _disposed;

            public TrackingLease(TrackingModuleWorkLeaseManager owner)
            {
                _owner = owner;
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.ReleaseCount++;
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}
