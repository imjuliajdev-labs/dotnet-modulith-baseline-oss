using Admin.Application.Authorization;
using Admin.Application.ProcessManagers;
using BuildingBlocks.Application.ProcessManagers;
using NodaTime;

namespace Admin.Infrastructure.ProcessManagers;

internal sealed class AdminAnnouncementProjectionProcessManagerStore : IAdminAnnouncementProjectionProcessManagerStore
{
    private readonly IProcessManagerCheckpointStore _checkpointStore;

    public AdminAnnouncementProjectionProcessManagerStore(IProcessManagerCheckpointStore checkpointStore)
    {
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
    }

    public ValueTask<ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState>?> LoadAsync(
        Guid announcementId,
        CancellationToken cancellationToken)
    {
        return _checkpointStore.LoadAsync<AdminAnnouncementProjectionProcessState>(
            AdminModuleInfo.ModuleKey,
            AdminAnnouncementProjectionProcessManager.ProcessManagerName,
            announcementId.ToString("N"),
            cancellationToken);
    }

    public ValueTask<IReadOnlyCollection<ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState>>> ListRecoverableAsync(
        Instant updatedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        return _checkpointStore.ListAsync<AdminAnnouncementProjectionProcessState>(
            AdminModuleInfo.ModuleKey,
            AdminAnnouncementProjectionProcessManager.ProcessManagerName,
            [ProcessManagerLifecycleState.Failed, ProcessManagerLifecycleState.Running],
            updatedBefore,
            limit,
            cancellationToken);
    }

    public ValueTask<ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState>> SaveAsync(
        ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState> checkpoint,
        CancellationToken cancellationToken)
    {
        return _checkpointStore.SaveAsync(checkpoint, cancellationToken);
    }
}
