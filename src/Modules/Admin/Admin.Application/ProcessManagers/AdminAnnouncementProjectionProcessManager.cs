using Admin.Application.Authorization;
using Admin.Application.Consumers;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.ProcessManagers;
using BuildingBlocks.Application.Results;
using Admin.Application.Realtime;
using Admin.PublicContracts.Queries;
using NodaTime;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace Admin.Application.ProcessManagers;

public sealed record AdminAnnouncementProjectionProcessState(
    Guid AnnouncementId,
    string Title,
    string Body,
    DateTimeOffset PublishedUtc,
    string PublishedByActorId,
    string SourceModuleKey,
    string? SourceReference,
    string Step,
    bool ProjectionStored,
    string? FailureCode);

public interface IAdminAnnouncementProjectionProcessManagerStore
{
    ValueTask<ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState>?> LoadAsync(
        Guid announcementId,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState>>> ListRecoverableAsync(
        Instant updatedBefore,
        int limit,
        CancellationToken cancellationToken);

    ValueTask<ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState>> SaveAsync(
        ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState> checkpoint,
        CancellationToken cancellationToken);
}

public interface IAdminAnnouncementProjectionProcessManager
{
    Task HandleAsync(AdminAnnouncementProjection announcement, CancellationToken cancellationToken);
}

public sealed record RecoverAdminAnnouncementProjectionCommand(AdminAnnouncementProjection Projection)
    : ICommand, IModuleScoped
{
    public string ModuleKey => AdminModuleInfo.ModuleKey;
}

internal sealed class RecoverAdminAnnouncementProjectionCommandHandler : ICommandHandler<RecoverAdminAnnouncementProjectionCommand>
{
    private readonly IAdminAnnouncementProjectionProcessManager _processManager;

    public RecoverAdminAnnouncementProjectionCommandHandler(IAdminAnnouncementProjectionProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    }

    public async Task<Result> Handle(RecoverAdminAnnouncementProjectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Projection);

        await _processManager.HandleAsync(command.Projection, cancellationToken);
        return Result.Success();
    }
}

public sealed class AdminAnnouncementProjectionProcessManager : IAdminAnnouncementProjectionProcessManager
{
    public const string ProcessManagerName = "AdminAnnouncementProjectionProcessManager";

    private static readonly Error ProjectionFailed = new(
        "admin.announcement_projection_failed",
        "Admin announcement projection process failed.",
        ErrorKind.Failure);

    private readonly DomainClock _clock;
    private readonly IAdminAnnouncementInbox _inbox;
    private readonly IAdminAnnouncementRealtimeNotifier _realtimeNotifier;
    private readonly IAdminAnnouncementProjectionProcessManagerStore _store;

    public AdminAnnouncementProjectionProcessManager(
        DomainClock clock,
        IAdminAnnouncementInbox inbox,
        IAdminAnnouncementRealtimeNotifier realtimeNotifier,
        IAdminAnnouncementProjectionProcessManagerStore store)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _realtimeNotifier = realtimeNotifier ?? throw new ArgumentNullException(nameof(realtimeNotifier));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task HandleAsync(AdminAnnouncementProjection announcement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        var checkpoint = await _store.LoadAsync(announcement.AnnouncementId, cancellationToken);
        if (checkpoint is { LifecycleState: ProcessManagerLifecycleState.Completed })
        {
            return;
        }

        checkpoint ??= await _store.SaveAsync(
            new ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState>(
                AdminModuleInfo.ModuleKey,
                ProcessManagerName,
                CreateProcessId(announcement.AnnouncementId),
                ProcessManagerLifecycleState.Running,
                Version: 0,
                UpdatedAt: _clock.GetCurrentInstant(),
                State: CreateState(
                    announcement,
                    step: "project-announcement",
                    projectionStored: false,
                    failureCode: null)),
            cancellationToken);

        try
        {
            await _inbox.StoreAsync(CreateProjection(checkpoint.State), cancellationToken);

            var completedAt = _clock.GetCurrentInstant();
            var completedState = checkpoint.State with
            {
                Step = "projection-stored",
                ProjectionStored = true,
                FailureCode = null
            };

            await _store.SaveAsync(
                checkpoint with
                {
                    LifecycleState = ProcessManagerLifecycleState.Completed,
                    UpdatedAt = completedAt,
                    CompletedAt = completedAt,
                    Failure = null,
                    State = completedState
                },
                cancellationToken);

            try
            {
                await _realtimeNotifier.PublishProjectedAsync(
                    AdminAnnouncementProjectedNotification.From(CreateReadModel(completedState)),
                    cancellationToken);
            }
            catch
            {
            }
        }
        catch (Exception exception)
        {
            var failedAt = _clock.GetCurrentInstant();

            try
            {
                await _store.SaveAsync(
                    checkpoint with
                    {
                        LifecycleState = ProcessManagerLifecycleState.Failed,
                        UpdatedAt = failedAt,
                        CompletedAt = null,
                        Failure = ProjectionFailed with { Message = exception.Message },
                        State = checkpoint.State with
                        {
                            Step = "projection-failed",
                            FailureCode = ProjectionFailed.Code
                        }
                    },
                    cancellationToken);
            }
            catch
            {
            }

            throw;
        }
    }

    private static string CreateProcessId(Guid announcementId)
    {
        return announcementId.ToString("N");
    }

    private static AdminAnnouncementProjection CreateProjection(AdminAnnouncementProjectionProcessState state)
    {
        return new AdminAnnouncementProjection(
            state.AnnouncementId,
            state.Title,
            state.Body,
            state.PublishedUtc,
            state.PublishedByActorId,
            state.SourceModuleKey,
            state.SourceReference);
    }

    private static AdminAnnouncementProjectionProcessState CreateState(
        AdminAnnouncementProjection announcement,
        string step,
        bool projectionStored,
        string? failureCode)
    {
        return new AdminAnnouncementProjectionProcessState(
            announcement.AnnouncementId,
            announcement.Title,
            announcement.Body,
            announcement.PublishedUtc,
            announcement.PublishedByActorId,
            announcement.SourceModuleKey,
            announcement.SourceReference,
            step,
            projectionStored,
            failureCode);
    }

    private static AdminAnnouncementReadModel CreateReadModel(AdminAnnouncementProjectionProcessState state)
    {
        return new AdminAnnouncementReadModel(
            state.AnnouncementId,
            state.Title,
            state.Body,
            state.PublishedUtc,
            state.PublishedByActorId)
        {
            SourceModuleKey = state.SourceModuleKey,
            SourceReference = state.SourceReference
        };
    }
}
