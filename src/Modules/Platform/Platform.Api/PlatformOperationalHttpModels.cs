using BuildingBlocks.Application;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Domain.Modules;
using Platform.Application.Auditing;
using Platform.Application.Health;

namespace Platform.Api;

public sealed record OperationalHealthSummaryResponse(
    string OverallStatus,
    int TotalModules,
    int EnabledModules,
    int DisabledModules,
    IReadOnlyCollection<OperationalModuleHealthResponse> Modules)
{
    public static OperationalHealthSummaryResponse From(OperationalHealthSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new OperationalHealthSummaryResponse(
            PlatformOperationalHttpValueFormatter.ToHttpValue(summary.OverallStatus),
            summary.TotalModules,
            summary.EnabledModules,
            summary.DisabledModules,
            summary.Modules.Select(OperationalModuleHealthResponse.From).ToArray());
    }
}

public sealed record OperationalModuleHealthResponse(
    string Key,
    string DisplayName,
    bool IsCritical,
    string RuntimeState,
    string Status)
{
    public static OperationalModuleHealthResponse From(OperationalModuleHealth module)
    {
        ArgumentNullException.ThrowIfNull(module);

        return new OperationalModuleHealthResponse(
            module.ModuleKey,
            module.DisplayName,
            module.IsCritical,
            PlatformOperationalHttpValueFormatter.ToHttpValue(module.RuntimeState),
            PlatformOperationalHttpValueFormatter.ToHttpValue(module.Status));
    }
}

public sealed record PlatformAuditTrailResponse(IReadOnlyCollection<PlatformAuditEntryResponse> Events)
{
    public static PlatformAuditTrailResponse From(PlatformAuditTrail trail)
    {
        ArgumentNullException.ThrowIfNull(trail);

        return new PlatformAuditTrailResponse(
            trail.Events.Select(PlatformAuditEntryResponse.From).ToArray());
    }
}

public sealed record PlatformAuditEntryResponse(
    string ModuleKey,
    string Action,
    string TargetType,
    string TargetId,
    string Outcome,
    DateTimeOffset OccurredUtc,
    string? ActorId,
    string? CorrelationId)
{
    public static PlatformAuditEntryResponse From(PlatformAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new PlatformAuditEntryResponse(
            entry.ModuleKey,
            entry.Action,
            entry.TargetType,
            entry.TargetId,
            entry.Outcome,
            entry.OccurredUtc.ToDateTimeOffset(),
            entry.ActorId,
            entry.CorrelationId);
    }
}

public sealed record PlatformAuditTrailPagedResponse(IReadOnlyCollection<PlatformAuditEntryResponse> Events, string? NextCursor)
{
    public static PlatformAuditTrailPagedResponse From(CursorPagedResult<PlatformAuditEntry> page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new PlatformAuditTrailPagedResponse(
            page.Items.Select(PlatformAuditEntryResponse.From).ToArray(),
            page.NextCursor);
    }
}

internal static class PlatformOperationalHttpValueFormatter
{
    public static string ToHttpValue(OperationalHealthStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }

    public static string ToHttpValue(ModuleRuntimeState runtimeState)
    {
        return runtimeState.ToString().ToLowerInvariant();
    }
}
