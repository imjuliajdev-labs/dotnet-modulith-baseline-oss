using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Dispatching;
using NodaTime;
using SampleFeature.Application.Authorization;

namespace SampleFeature.Application.Publishing;

internal static class SampleAnnouncementAuditing
{
    public const string TargetTypeAnnouncement = "sample-announcement";
    public const string TargetTypeScheduledAnnouncement = "sample-scheduled-announcement";

    public const string ActionPublish = "sample-feature.announcement.publish";
    public const string ActionSchedule = "sample-feature.announcement.schedule";

    public const string OutcomePublished = "published";
    public const string OutcomeScheduled = "scheduled";

    public static Task WriteAsync(
        IAuditEventWriter auditEventWriter,
        IRequestContextAccessor requestContextAccessor,
        string actorId,
        Instant occurredUtc,
        string action,
        string targetType,
        string targetId,
        string outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEventWriter);
        ArgumentNullException.ThrowIfNull(requestContextAccessor);

        return auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: SampleFeatureModuleInfo.ModuleKey,
                Action: action,
                TargetType: targetType,
                TargetId: targetId,
                Outcome: outcome,
                OccurredUtc: occurredUtc,
                ActorId: actorId,
                CorrelationId: requestContextAccessor.Current?.CorrelationId),
            cancellationToken);
    }
}
