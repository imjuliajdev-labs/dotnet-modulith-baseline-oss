using NodaTime;

namespace BuildingBlocks.Application.Auditing;

public sealed record AuditEvent(
    string ModuleKey,
    string Action,
    string TargetType,
    string TargetId,
    string Outcome,
    Instant OccurredUtc,
    string? ActorId = null,
    string? CorrelationId = null);

public interface IAuditEventWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}
