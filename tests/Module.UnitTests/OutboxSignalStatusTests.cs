using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using BuildingBlocks.Infrastructure.Telemetry;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using Npgsql;
using IClock = BuildingBlocks.Domain.Time.IClock;

namespace Module.UnitTests;

public sealed class OutboxSignalStatusTests
{
    [Fact]
    public async Task UnknownStatusIsReportedAsDegradedUntilTheHostedServiceFlipsIt()
    {
        var status = new IntegrationEventOutboxSignalStatus(new FixedClock(Instant.FromUtc(2026, 4, 14, 9, 0)));
        var health = new OutboxSignalHealthCheck(status);

        var result = await health.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(OutboxSignalState.Unknown, status.State);
        Assert.False(status.IsDegraded);
    }

    [Fact]
    public async Task MarkListeningMovesTheStatusToHealthy()
    {
        var clock = new FixedClock(Instant.FromUtc(2026, 4, 14, 9, 0));
        var status = new IntegrationEventOutboxSignalStatus(clock);
        var health = new OutboxSignalHealthCheck(status);

        status.MarkListening();

        var result = await health.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(OutboxSignalState.Listening, status.State);
        Assert.False(status.IsDegraded);
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(Instant.FromUtc(2026, 4, 14, 9, 0), status.LastTransition);
        Assert.Null(status.LastFailure);
    }

    [Fact]
    public async Task MarkDegradedFlipsStatusAndSurfacesReasonThroughHealthCheck()
    {
        var clock = new FixedClock(Instant.FromUtc(2026, 4, 14, 9, 0));
        var status = new IntegrationEventOutboxSignalStatus(clock);
        var health = new OutboxSignalHealthCheck(status);

        status.MarkListening();
        clock.Advance(Duration.FromSeconds(5));
        status.MarkDegraded("postgres listener socket closed");

        var result = await health.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(OutboxSignalState.Degraded, status.State);
        Assert.True(status.IsDegraded);
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("postgres listener socket closed", result.Description);
        Assert.Equal(Instant.FromUtc(2026, 4, 14, 9, 0, 5), status.LastTransition);
    }

    [Fact]
    public void MarkDegradedRejectsEmptyReasonSoTheReasonIsAlwaysOperatorActionable()
    {
        var status = new IntegrationEventOutboxSignalStatus(new FixedClock(Instant.FromUtc(2026, 4, 14, 9, 0)));

        Assert.Throws<ArgumentException>(() => status.MarkDegraded(string.Empty));
        Assert.Throws<ArgumentException>(() => status.MarkDegraded("   "));
    }

    [Fact]
    public async Task HostedServiceFlipsStatusToDegradedWhenStartListeningFailsAndStillDrainsStores()
    {
        var clock = new FixedClock(Instant.FromUtc(2026, 4, 14, 9, 0));
        var status = new IntegrationEventOutboxSignalStatus(clock);
        var metrics = new OutboxMetrics(status);
        var fakeStore = new FakeOutboxStore("alpha");
        var fakeDispatcher = new CountingDispatcher();

        // Unreachable Postgres endpoint with a tiny timeout so StartListeningAsync fails fast at socket open.
        var unreachableConnectionString = "Host=127.0.0.1;Port=1;Username=none;Password=none;Database=none;Timeout=1;Command Timeout=1";
        await using var dataSource = NpgsqlDataSource.Create(unreachableConnectionString);
        var signal = new IntegrationEventOutboxSignal(
            dataSource,
            NullLogger<IntegrationEventOutboxSignal>.Instance);
        var holder = new IntegrationEventOutboxSignalHolder(signal);

        var hostedService = new IntegrationEventOutboxHostedService(
            new[] { (IIntegrationEventOutboxStore)fakeStore },
            fakeDispatcher,
            new IntegrationEventOutboxProcessingOptions { PollInterval = TimeSpan.FromMilliseconds(25) },
            NullLogger<IntegrationEventOutboxHostedService>.Instance,
            status,
            metrics,
            holder);

        using var cts = new CancellationTokenSource();
        await hostedService.StartAsync(cts.Token);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (status.State != OutboxSignalState.Degraded || fakeDispatcher.Invocations == 0)
        {
            if (deadline.Token.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(25, deadline.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await cts.CancelAsync();
        await hostedService.StopAsync(CancellationToken.None);
        await signal.DisposeAsync();

        Assert.Equal(OutboxSignalState.Degraded, status.State);
        Assert.False(string.IsNullOrWhiteSpace(status.LastFailure));
        Assert.True(fakeDispatcher.Invocations > 0, "Hosted service must continue polling even though the signal listener failed.");

        var health = new OutboxSignalHealthCheck(status);
        var result = await health.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    private sealed class FakeOutboxStore : IIntegrationEventOutboxStore
    {
        public FakeOutboxStore(string moduleKey)
        {
            ModuleKey = moduleKey;
        }

        public string ModuleKey { get; }

        public ValueTask EnqueueAsync(IntegrationEventOutboxPublishRequest request, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyCollection<IntegrationEventOutboxLeasedMessage>> LeaseAvailableAsync(
            int batchSize,
            Instant now,
            Instant leaseUntil,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyCollection<IntegrationEventOutboxLeasedMessage>>(Array.Empty<IntegrationEventOutboxLeasedMessage>());

        public ValueTask MarkDispatchedAsync(long messageId, Instant processedAt, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask MarkFailedAsync(
            long messageId,
            Instant failedAt,
            Instant nextAvailableAt,
            bool deadLettered,
            Error error,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyCollection<IntegrationEventOutboxDeadLetterEntry>> GetDeadLetteredAsync(int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyCollection<IntegrationEventOutboxDeadLetterEntry>>(Array.Empty<IntegrationEventOutboxDeadLetterEntry>());
    }

    private sealed class CountingDispatcher : IIntegrationEventOutboxDispatcher
    {
        private int _invocations;

        public int Invocations => _invocations;

        public Task<int> DispatchAvailableAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _invocations);
            return Task.FromResult(0);
        }

        public Task<int> DispatchAvailableForModuleAsync(string moduleKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _invocations);
            return Task.FromResult(0);
        }
    }

    private sealed class FixedClock : IClock
    {
        private Instant _now;

        public FixedClock(Instant now)
        {
            _now = now;
        }

        public Instant GetCurrentInstant() => _now;

        public void Advance(Duration amount) => _now += amount;
    }
}
