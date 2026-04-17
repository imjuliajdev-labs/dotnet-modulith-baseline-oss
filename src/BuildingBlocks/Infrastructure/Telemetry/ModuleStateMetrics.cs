using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BuildingBlocks.Infrastructure.Telemetry;

public sealed class ModuleStateMetrics : IDisposable
{
    public const string MeterName = "BuildingBlocks.Modules";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _stateTransitions;

    public ModuleStateMetrics()
    {
        _stateTransitions = _meter.CreateCounter<long>(
            "module.state.transitions",
            description: "Total number of module state transitions.");
    }

    public void RecordTransition(string moduleKey, string fromState, string toState)
    {
        _stateTransitions.Add(1, new TagList
        {
            { "module.key", moduleKey },
            { "from_state", fromState },
            { "to_state", toState },
        });
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
