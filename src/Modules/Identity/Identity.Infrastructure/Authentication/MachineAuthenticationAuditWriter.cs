using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Dispatching;
using Identity.Application.Authorization;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Authentication;

public sealed class MachineAuthenticationAuditWriter
{
    private const string MachineAuthenticationAuditAction = "identity.machine_auth.authenticate";

    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ILogger<MachineAuthenticationAuditWriter> _logger;
    private readonly IRequestContextAccessor _requestContextAccessor;

    public MachineAuthenticationAuditWriter(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ILogger<MachineAuthenticationAuditWriter> logger,
        IRequestContextAccessor requestContextAccessor)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
    }

    public async Task WriteAsync(string targetId, string outcome, string? actorId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);

        try
        {
            await _auditEventWriter.WriteAsync(
                new AuditEvent(
                    ModuleKey: IdentityModuleInfo.ModuleKey,
                    Action: MachineAuthenticationAuditAction,
                    TargetType: "machine_client",
                    TargetId: string.IsNullOrWhiteSpace(targetId) ? "unknown" : targetId,
                    Outcome: outcome,
                    OccurredUtc: _clock.GetCurrentInstant(),
                    ActorId: actorId,
                    CorrelationId: _requestContextAccessor.Current?.CorrelationId),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Machine authentication audit write failed.");
        }
    }
}
