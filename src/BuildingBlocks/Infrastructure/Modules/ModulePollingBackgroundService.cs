using BuildingBlocks.Application.Modules;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.Modules;

public abstract class ModulePollingBackgroundService : BackgroundService
{
    private readonly IModuleExecutionGate _moduleExecutionGate;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;

    protected ModulePollingBackgroundService(
        IModuleExecutionGate moduleExecutionGate,
        TimeSpan pollInterval,
        ILogger logger)
    {
        _moduleExecutionGate = moduleExecutionGate ?? throw new ArgumentNullException(nameof(moduleExecutionGate));
        _pollInterval = pollInterval > TimeSpan.Zero ? pollInterval : throw new ArgumentOutOfRangeException(nameof(pollInterval));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected abstract string ModuleKey { get; }

    protected abstract Task ExecuteEnabledIterationAsync(CancellationToken stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var execution = await _moduleExecutionGate.TryEnterAsync(ModuleKey, stoppingToken);
                if (execution.IsEntered)
                {
                    await ExecuteEnabledIterationAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Module worker {WorkerName} failed while processing module {ModuleKey}.",
                    GetType().Name,
                    ModuleKey);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
