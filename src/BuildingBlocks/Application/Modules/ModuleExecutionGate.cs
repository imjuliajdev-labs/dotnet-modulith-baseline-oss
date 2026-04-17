using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Modules;

public interface IModuleExecutionGate
{
    ValueTask<ModuleExecutionLease> TryEnterAsync(string moduleKey, CancellationToken cancellationToken);
}

public readonly struct ModuleExecutionLease : IAsyncDisposable
{
    private readonly IAsyncDisposable? _lease;

    internal ModuleExecutionLease(Error failure, IAsyncDisposable? lease)
    {
        Failure = failure;
        _lease = lease;
    }

    public Error Failure { get; }

    public bool IsEntered => Failure == Error.None;

    public async ValueTask DisposeAsync()
    {
        if (_lease is not null)
        {
            await _lease.DisposeAsync();
        }
    }

    internal static ModuleExecutionLease Denied(Error failure)
    {
        if (failure == Error.None)
        {
            throw new ArgumentException("A denied module execution lease requires a concrete failure.", nameof(failure));
        }

        return new ModuleExecutionLease(failure, lease: null);
    }

    internal static ModuleExecutionLease Entered(IAsyncDisposable lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return new ModuleExecutionLease(Error.None, lease);
    }
}

public sealed class ModuleExecutionGate : IModuleExecutionGate
{
    private readonly IModuleStateGuard _moduleStateGuard;
    private readonly IModuleWorkLeaseManager _moduleWorkLeaseManager;

    public ModuleExecutionGate(IModuleStateGuard moduleStateGuard, IModuleWorkLeaseManager moduleWorkLeaseManager)
    {
        _moduleStateGuard = moduleStateGuard ?? throw new ArgumentNullException(nameof(moduleStateGuard));
        _moduleWorkLeaseManager = moduleWorkLeaseManager ?? throw new ArgumentNullException(nameof(moduleWorkLeaseManager));
    }

    public async ValueTask<ModuleExecutionLease> TryEnterAsync(string moduleKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        var failure = await _moduleStateGuard.GetFailureOrNoneAsync(moduleKey, cancellationToken);
        if (failure != Error.None)
        {
            return ModuleExecutionLease.Denied(failure);
        }

        var lease = await _moduleWorkLeaseManager.AcquireAsync(moduleKey, cancellationToken);

        failure = await _moduleStateGuard.GetFailureOrNoneAsync(moduleKey, cancellationToken);
        if (failure != Error.None)
        {
            await lease.DisposeAsync();
            return ModuleExecutionLease.Denied(failure);
        }

        return ModuleExecutionLease.Entered(lease);
    }
}
