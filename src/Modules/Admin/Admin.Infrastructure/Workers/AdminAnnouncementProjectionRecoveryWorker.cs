using Admin.Application.Authorization;
using Admin.Application.Consumers;
using Admin.Application.ProcessManagers;
using Admin.Infrastructure.Configuration;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace Admin.Infrastructure.Workers;

internal sealed class AdminAnnouncementProjectionRecoveryWorker : ModulePollingBackgroundService
{
    private readonly DomainClock _clock;
    private readonly ILogger<AdminAnnouncementProjectionRecoveryWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAdminAnnouncementProjectionProcessManagerStore _store;
    private readonly int _batchSize;
    private readonly Duration _staleAfter;

    public AdminAnnouncementProjectionRecoveryWorker(
        DomainClock clock,
        IServiceScopeFactory scopeFactory,
        BuildingBlocks.Application.Modules.IModuleExecutionGate moduleExecutionGate,
        IAdminAnnouncementProjectionProcessManagerStore store,
        IOptions<AdminInfrastructureOptions> options,
        ILogger<AdminAnnouncementProjectionRecoveryWorker> logger)
        : base(moduleExecutionGate, ResolvePollInterval(options).ToTimeSpan(), logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var worker = options.Value.ProjectionRecoveryWorker;
        _batchSize = worker.BatchSize;
        _staleAfter = Duration.FromMilliseconds(worker.StaleAfterMilliseconds);
    }

    protected override string ModuleKey => AdminModuleInfo.ModuleKey;

    protected override async Task ExecuteEnabledIterationAsync(CancellationToken stoppingToken)
    {
        var updatedBefore = _clock.GetCurrentInstant() - _staleAfter;
        var checkpoints = await _store.ListRecoverableAsync(updatedBefore, _batchSize, stoppingToken);

        foreach (var checkpoint in checkpoints)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                var result = await dispatcher.Send(
                    new RecoverAdminAnnouncementProjectionCommand(ToProjection(checkpoint.State)),
                    stoppingToken);

                if (result.IsFailure)
                {
                    _logger.LogWarning(
                        "Admin announcement recovery failed for announcement {AnnouncementId}: {ErrorCode} {ErrorMessage}",
                        checkpoint.State.AnnouncementId,
                        result.Error.Code,
                        result.Error.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Admin announcement recovery failed for announcement {AnnouncementId}.",
                    checkpoint.State.AnnouncementId);
            }
        }
    }

    private static Duration ResolvePollInterval(IOptions<AdminInfrastructureOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Duration.FromMilliseconds(options.Value.ProjectionRecoveryWorker.PollIntervalMilliseconds);
    }

    private static AdminAnnouncementProjection ToProjection(AdminAnnouncementProjectionProcessState state)
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
}
