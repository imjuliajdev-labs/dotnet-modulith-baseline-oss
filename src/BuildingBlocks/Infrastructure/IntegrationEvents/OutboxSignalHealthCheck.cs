using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

/// <summary>
/// Reports the outbox LISTEN/NOTIFY listener as <see cref="HealthStatus.Degraded"/> whenever the hosted service
/// failed to start or lost its connection. The dispatcher still drains the outbox via polling in that mode, so the
/// system is not Unhealthy — but operators need to see that the low-latency signal path is down.
/// </summary>
public sealed class OutboxSignalHealthCheck : IHealthCheck
{
    public const string Name = "outbox_signal";

    private readonly IntegrationEventOutboxSignalStatus _status;

    public OutboxSignalHealthCheck(IntegrationEventOutboxSignalStatus status)
    {
        _status = status ?? throw new ArgumentNullException(nameof(status));
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return _status.State switch
        {
            OutboxSignalState.Listening => Task.FromResult(HealthCheckResult.Healthy(
                "Outbox signal listener is active.")),
            OutboxSignalState.Degraded => Task.FromResult(HealthCheckResult.Degraded(
                $"Outbox signal listener is degraded; dispatcher is in poll-only mode. Last failure: {_status.LastFailure ?? "unknown"}")),
            _ => Task.FromResult(HealthCheckResult.Degraded(
                "Outbox signal listener state has not been reported yet.")),
        };
    }
}
