using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Dispatching;

namespace Module.UnitTests.Support;

internal sealed class RecordingAuditEventWriter : IAuditEventWriter
{
    public List<AuditEvent> Events { get; } = [];

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}

internal sealed class StubCurrentActorAccessor : ICurrentActorAccessor
{
    private readonly CurrentActor _actor;

    public StubCurrentActorAccessor(CurrentActor actor)
    {
        _actor = actor;
    }

    public ValueTask<CurrentActor> GetCurrentAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(_actor);
    }
}

internal sealed class InMemoryRequestContextAccessor : IRequestContextAccessor
{
    public RequestContext? Current { get; set; }
}
