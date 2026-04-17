using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BuildingBlocks.Infrastructure.Telemetry;

public sealed class DispatcherMetrics : IDisposable
{
    public const string MeterName = "BuildingBlocks.Dispatcher";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _requestsTotal;
    private readonly Histogram<double> _requestDuration;

    public DispatcherMetrics()
    {
        _requestsTotal = _meter.CreateCounter<long>(
            "dispatcher.requests.total",
            description: "Total number of dispatcher requests processed.");

        _requestDuration = _meter.CreateHistogram<double>(
            "dispatcher.request.duration",
            unit: "ms",
            description: "Duration of dispatcher request processing in milliseconds.");
    }

    public void RecordRequest(string requestKind, string? moduleKey, string outcome, double durationMs)
    {
        var tags = new TagList
        {
            { "request.kind", requestKind },
            { "module.key", moduleKey ?? "unknown" },
            { "outcome", outcome },
        };

        _requestsTotal.Add(1, tags);
        _requestDuration.Record(durationMs, new TagList
        {
            { "request.kind", requestKind },
            { "module.key", moduleKey ?? "unknown" },
        });
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
