using BuildingBlocks.Application.Results;
using NodaTime;

namespace BuildingBlocks.Application.ProcessManagers;

public enum ProcessManagerLifecycleState
{
    Running = 0,
    Completed = 1,
    Failed = 2,
    Compensating = 3,
    Compensated = 4
}

public sealed record ProcessManagerCheckpoint<TState>(
    string ModuleKey,
    string ProcessManagerName,
    string ProcessId,
    ProcessManagerLifecycleState LifecycleState,
    int Version,
    Instant UpdatedAt,
    TState State,
    Instant? CompletedAt = null,
    Error? Failure = null);

public interface IProcessManagerCheckpointStore
{
    string ModuleKey { get; }

    ValueTask<ProcessManagerCheckpoint<TState>?> LoadAsync<TState>(
        string moduleKey,
        string processManagerName,
        string processId,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<ProcessManagerCheckpoint<TState>>> ListAsync<TState>(
        string moduleKey,
        string processManagerName,
        IReadOnlyCollection<ProcessManagerLifecycleState> lifecycleStates,
        Instant? updatedBefore,
        int limit,
        CancellationToken cancellationToken);

    ValueTask<ProcessManagerCheckpoint<TState>> SaveAsync<TState>(
        ProcessManagerCheckpoint<TState> checkpoint,
        CancellationToken cancellationToken);
}
