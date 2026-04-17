using BuildingBlocks.Domain.Time;
using NodaTime;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

/// <summary>
/// Shared, thread-safe status object for the outbox signal listener. The hosted service flips this to
/// <see cref="OutboxSignalState.Listening"/> on successful <c>LISTEN</c> startup and to
/// <see cref="OutboxSignalState.Degraded"/> if the LISTEN/NOTIFY path fails. The health check and the
/// observable gauge both read from this one instance so operators cannot miss a silent fallback to poll-only mode.
/// </summary>
public sealed class IntegrationEventOutboxSignalStatus
{
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly object _sync = new();
    private OutboxSignalState _state = OutboxSignalState.Unknown;
    private string? _lastFailure;
    private Instant? _lastTransition;

    public IntegrationEventOutboxSignalStatus(BuildingBlocks.Domain.Time.IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public OutboxSignalState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public string? LastFailure
    {
        get
        {
            lock (_sync)
            {
                return _lastFailure;
            }
        }
    }

    public Instant? LastTransition
    {
        get
        {
            lock (_sync)
            {
                return _lastTransition;
            }
        }
    }

    public bool IsDegraded => State == OutboxSignalState.Degraded;

    public void MarkListening()
    {
        var now = _clock.GetCurrentInstant();
        lock (_sync)
        {
            _state = OutboxSignalState.Listening;
            _lastFailure = null;
            _lastTransition = now;
        }
    }

    public void MarkDegraded(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var now = _clock.GetCurrentInstant();
        lock (_sync)
        {
            _state = OutboxSignalState.Degraded;
            _lastFailure = reason;
            _lastTransition = now;
        }
    }
}

public enum OutboxSignalState
{
    Unknown = 0,
    Listening = 1,
    Degraded = 2,
}
