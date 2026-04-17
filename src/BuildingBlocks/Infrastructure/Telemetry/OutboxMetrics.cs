using System.Diagnostics;
using System.Diagnostics.Metrics;
using BuildingBlocks.Infrastructure.IntegrationEvents;

namespace BuildingBlocks.Infrastructure.Telemetry;

public sealed class OutboxMetrics : IDisposable
{
    public const string MeterName = "BuildingBlocks.Outbox";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _eventsPublished;
    private readonly Counter<long> _eventsDispatched;
    private readonly Counter<long> _eventsDeadLettered;
    // ObservableGauge instances do not need to be stored after creation — the Meter keeps them alive — but we hold a
    // reference so disposal is explicit and the gauge appears in the meter inventory for diagnostics.
#pragma warning disable IDE0052
    private readonly ObservableGauge<int>? _signalDegradedGauge;
#pragma warning restore IDE0052

    public OutboxMetrics(IntegrationEventOutboxSignalStatus? signalStatus = null)
    {
        _eventsPublished = _meter.CreateCounter<long>(
            "outbox.events.published",
            description: "Total number of integration events published to the outbox.");

        _eventsDispatched = _meter.CreateCounter<long>(
            "outbox.events.dispatched",
            description: "Total number of integration events dispatched from the outbox.");

        _eventsDeadLettered = _meter.CreateCounter<long>(
            "outbox.events.dead_lettered",
            description: "Total number of integration events dead-lettered.");

        if (signalStatus is not null)
        {
            _signalDegradedGauge = _meter.CreateObservableGauge(
                "outbox.signal.degraded",
                observeValue: () => signalStatus.IsDegraded ? 1 : 0,
                unit: "{state}",
                description: "1 when the outbox LISTEN/NOTIFY listener is in degraded (poll-only) mode; 0 when it is active.");
        }
    }

    public void RecordPublished(string moduleKey, string eventType)
    {
        _eventsPublished.Add(1, new TagList
        {
            { "module.key", moduleKey },
            { "event.type", eventType },
        });
    }

    public void RecordDispatched(string moduleKey, string outcome)
    {
        _eventsDispatched.Add(1, new TagList
        {
            { "module.key", moduleKey },
            { "outcome", outcome },
        });
    }

    public void RecordDeadLettered(string moduleKey, string eventType)
    {
        _eventsDeadLettered.Add(1, new TagList
        {
            { "module.key", moduleKey },
            { "event.type", eventType },
        });
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
