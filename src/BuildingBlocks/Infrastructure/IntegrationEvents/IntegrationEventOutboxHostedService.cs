using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

public sealed class IntegrationEventOutboxHostedService : BackgroundService
{
    private readonly IIntegrationEventOutboxDispatcher _dispatcher;
    private readonly ILogger<IntegrationEventOutboxHostedService> _logger;
    private readonly IntegrationEventOutboxProcessingOptions _options;
    private readonly IReadOnlyCollection<IIntegrationEventOutboxStore> _stores;
    private readonly IntegrationEventOutboxSignal? _signal;
    private readonly IntegrationEventOutboxSignalStatus _signalStatus;
#pragma warning disable IDE0052
    // OutboxMetrics owns the `outbox.signal.degraded` ObservableGauge and other outbox-level counters. The hosted
    // service takes it as a dependency purely to force construction at application start so the gauge callback is
    // wired before any emit window opens.
    private readonly OutboxMetrics _outboxMetrics;
#pragma warning restore IDE0052

    public IntegrationEventOutboxHostedService(
        IEnumerable<IIntegrationEventOutboxStore> stores,
        IIntegrationEventOutboxDispatcher dispatcher,
        IntegrationEventOutboxProcessingOptions options,
        ILogger<IntegrationEventOutboxHostedService> logger,
        IntegrationEventOutboxSignalStatus signalStatus,
        OutboxMetrics outboxMetrics,
        IntegrationEventOutboxSignalHolder? signalHolder = null)
    {
        ArgumentNullException.ThrowIfNull(stores);

        _stores = stores.ToArray();
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _signalStatus = signalStatus ?? throw new ArgumentNullException(nameof(signalStatus));
        _outboxMetrics = outboxMetrics ?? throw new ArgumentNullException(nameof(outboxMetrics));
        _signal = signalHolder?.Signal;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_stores.Count == 0)
        {
            return;
        }

        if (_signal is not null)
        {
            try
            {
                await _signal.StartListeningAsync(stoppingToken);
                _signalStatus.MarkListening();
                _logger.LogInformation("Outbox signal listener started on channel '{Channel}'.", IntegrationEventOutboxSignal.ChannelName);
            }
            catch (Exception exception)
            {
                _signalStatus.MarkDegraded(exception.Message);
                _logger.LogWarning(exception, "Failed to start outbox signal listener; falling back to poll-only mode.");
            }
        }
        else
        {
            // No signal wiring at all: there is nothing to listen on, so the pure polling path is the intended mode.
            _signalStatus.MarkListening();
        }

        var storePumps = _stores
            .Select(store => RunStorePumpAsync(store.ModuleKey, stoppingToken))
            .ToArray();

        await Task.WhenAll(storePumps);
    }

    private async Task RunStorePumpAsync(string moduleKey, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _dispatcher.DispatchAvailableForModuleAsync(moduleKey, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Integration-event outbox polling failed for module {ModuleKey}.", moduleKey);
            }

            try
            {
                if (_signal is not null)
                {
                    await _signal.WaitAsync(_options.PollInterval, stoppingToken);
                }
                else if (!await timer.WaitForNextTickAsync(stoppingToken))
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
