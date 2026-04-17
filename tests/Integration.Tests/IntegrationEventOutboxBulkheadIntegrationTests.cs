using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using BuildingBlocks.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Integration.Tests;

public sealed class IntegrationEventOutboxBulkheadIntegrationTests
{
    [Xunit.Fact]
    public async Task OutboxHostedServicePollsEachModuleOnIndependentLoops()
    {
        var alphaStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var betaStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAlpha = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<BuildingBlocks.Domain.Time.IClock>(new BuildingBlocks.Testing.Time.FakeClock(NodaTime.Instant.FromUtc(2026, 4, 14, 9, 0)));
        builder.Services.AddSingleton<IntegrationEventOutboxSignalStatus>();
        builder.Services.AddSingleton<OutboxMetrics>();
        builder.Services.AddSingleton<IIntegrationEventOutboxStore>(new FakeOutboxStore("alpha"));
        builder.Services.AddSingleton<IIntegrationEventOutboxStore>(new FakeOutboxStore("beta"));
        builder.Services.AddSingleton(new IntegrationEventOutboxProcessingOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(50)
        });
        builder.Services.AddSingleton<IIntegrationEventOutboxDispatcher>(new CoordinatedOutboxDispatcher(alphaStarted, betaStarted, releaseAlpha));
        builder.Services.AddHostedService<IntegrationEventOutboxHostedServiceProxy>();

        using var host = builder.Build();
        await host.StartAsync();

        await alphaStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await betaStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        releaseAlpha.TrySetResult();
        await host.StopAsync();
    }

    private sealed class CoordinatedOutboxDispatcher : IIntegrationEventOutboxDispatcher
    {
        private readonly TaskCompletionSource _alphaStarted;
        private readonly TaskCompletionSource _betaStarted;
        private readonly TaskCompletionSource _releaseAlpha;
        private int _alphaVisits;
        private int _betaVisits;

        public CoordinatedOutboxDispatcher(
            TaskCompletionSource alphaStarted,
            TaskCompletionSource betaStarted,
            TaskCompletionSource releaseAlpha)
        {
            _alphaStarted = alphaStarted;
            _betaStarted = betaStarted;
            _releaseAlpha = releaseAlpha;
        }

        public Task<int> DispatchAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public async Task<int> DispatchAvailableForModuleAsync(string moduleKey, CancellationToken cancellationToken = default)
        {
            if (string.Equals(moduleKey, "alpha", StringComparison.Ordinal))
            {
                if (Interlocked.CompareExchange(ref _alphaVisits, 1, 0) == 0)
                {
                    _alphaStarted.TrySetResult();
                    await _releaseAlpha.Task.WaitAsync(cancellationToken);
                }

                return 0;
            }

            if (string.Equals(moduleKey, "beta", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _betaVisits, 1, 0) == 0)
            {
                _betaStarted.TrySetResult();
            }

            return 0;
        }
    }

    private sealed class FakeOutboxStore : IIntegrationEventOutboxStore
    {
        public FakeOutboxStore(string moduleKey)
        {
            ModuleKey = moduleKey;
        }

        public string ModuleKey { get; }

        public ValueTask EnqueueAsync(IntegrationEventOutboxPublishRequest request, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyCollection<IntegrationEventOutboxLeasedMessage>> LeaseAvailableAsync(int batchSize, NodaTime.Instant now, NodaTime.Instant leaseUntil, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IReadOnlyCollection<IntegrationEventOutboxLeasedMessage>>(Array.Empty<IntegrationEventOutboxLeasedMessage>());
        }

        public ValueTask MarkDispatchedAsync(long messageId, NodaTime.Instant processedAt, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask MarkFailedAsync(long messageId, NodaTime.Instant failedAt, NodaTime.Instant nextAvailableAt, bool deadLettered, Error error, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyCollection<IntegrationEventOutboxDeadLetterEntry>> GetDeadLetteredAsync(int limit, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IReadOnlyCollection<IntegrationEventOutboxDeadLetterEntry>>(Array.Empty<IntegrationEventOutboxDeadLetterEntry>());
        }
    }

    private sealed class IntegrationEventOutboxHostedServiceProxy : IHostedService
    {
        private readonly IntegrationEventOutboxHostedService _inner;

        public IntegrationEventOutboxHostedServiceProxy(
            IEnumerable<IIntegrationEventOutboxStore> stores,
            IIntegrationEventOutboxDispatcher dispatcher,
            IntegrationEventOutboxProcessingOptions options,
            ILogger<IntegrationEventOutboxHostedService> logger,
            IntegrationEventOutboxSignalStatus signalStatus,
            OutboxMetrics outboxMetrics)
        {
            _inner = new IntegrationEventOutboxHostedService(stores, dispatcher, options, logger, signalStatus, outboxMetrics);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _inner.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _inner.StopAsync(cancellationToken);
        }
    }
}
