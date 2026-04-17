using BuildingBlocks.Application;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Modules;
using NodaTime;
using Platform.Application.Auditing;
using Platform.Application.Bootstrap;
using Platform.Application.ModuleState;
using Platform.Domain.ModuleActivation;

namespace Module.UnitTests.Platform;

internal static class PlatformTestData
{
    public static readonly Instant FixedNow = Instant.FromUtc(2026, 4, 16, 10, 0);

    public static ManagedModuleState CreateModuleState(
        string moduleKey,
        string displayName,
        bool canBeDisabled,
        ModuleDesiredState desiredState,
        ModuleRuntimeState runtimeState,
        long version = 1,
        string routePrefix = "/module",
        bool defaultEnabled = true,
        Guid? transitionId = null,
        Instant? updatedUtc = null,
        string? updatedByActorId = null,
        string? lastErrorCode = null,
        string? lastErrorDetail = null,
        IReadOnlyCollection<ManagedModuleStateChange>? changes = null)
    {
        return new ManagedModuleState(
            moduleKey,
            displayName,
            routePrefix,
            defaultEnabled,
            canBeDisabled,
            desiredState,
            runtimeState,
            version,
            transitionId,
            updatedUtc ?? FixedNow,
            updatedByActorId,
            lastErrorCode,
            lastErrorDetail,
            changes ?? []);
    }

    public static PlatformAuditEntry CreateAuditEntry(
        string moduleKey,
        string action,
        string targetType,
        string targetId,
        string outcome,
        Instant? occurredUtc = null,
        string? actorId = null,
        string? correlationId = null)
    {
        return new PlatformAuditEntry(
            moduleKey,
            action,
            targetType,
            targetId,
            outcome,
            occurredUtc ?? FixedNow,
            actorId,
            correlationId);
    }
}

internal sealed class FakeModule : IModule
{
    public FakeModule(ModuleDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public ModuleDescriptor Descriptor { get; }

    public string Key => Descriptor.Key;
}

internal sealed class RecordingPlatformModuleStateStore : IPlatformModuleStateStore
{
    public IReadOnlyCollection<ManagedModuleState> States { get; set; } = [];

    public ValueTask<IReadOnlyCollection<ManagedModuleState>> ListAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(States);
    }

    public ValueTask<ModuleStateChangeResult> EnableAsync(string moduleKey, string? actorId, Instant changedUtc, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public ValueTask<ModuleStateChangeResult> DisableAsync(string moduleKey, string? actorId, Instant changedUtc, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public ValueTask<ModuleStateSnapshot> GetRequiredStateAsync(string moduleKey, CancellationToken cancellationToken)
    {
        var snapshot = States.FirstOrDefault(s => string.Equals(s.ModuleKey, moduleKey, StringComparison.Ordinal));
        return ValueTask.FromResult(new ModuleStateSnapshot(moduleKey, snapshot?.RuntimeState ?? ModuleRuntimeState.Enabled));
    }
}

internal sealed class RecordingPlatformAuditEventReader : IPlatformAuditEventReader
{
    public CursorPagedResult<PlatformAuditEntry> PagedResult { get; set; } = new([], null);

    public int? LastLimit { get; private set; }

    public string? LastAfterCursor { get; private set; }

    public ValueTask<IReadOnlyCollection<PlatformAuditEntry>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlyCollection<PlatformAuditEntry>>(PagedResult.Items);
    }

    public ValueTask<CursorPagedResult<PlatformAuditEntry>> ListPagedAsync(int limit, string? afterCursor, CancellationToken cancellationToken)
    {
        LastLimit = limit;
        LastAfterCursor = afterCursor;
        return ValueTask.FromResult(PagedResult);
    }
}

internal sealed class RecordingPlatformOutboxStore : IIntegrationEventOutboxStore
{
    public RecordingPlatformOutboxStore(string moduleKey)
    {
        ModuleKey = moduleKey;
    }

    public string ModuleKey { get; }

    public IReadOnlyCollection<IntegrationEventOutboxDeadLetterEntry> DeadLetters { get; set; } = [];

    public int? LastDeadLetterLimit { get; private set; }

    public ValueTask EnqueueAsync(IntegrationEventOutboxPublishRequest request, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public ValueTask<IReadOnlyCollection<IntegrationEventOutboxLeasedMessage>> LeaseAvailableAsync(int batchSize, Instant now, Instant leaseUntil, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public ValueTask MarkDispatchedAsync(long messageId, Instant processedAt, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public ValueTask MarkFailedAsync(long messageId, Instant failedAt, Instant nextAvailableAt, bool deadLettered, Error error, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public ValueTask<IReadOnlyCollection<IntegrationEventOutboxDeadLetterEntry>> GetDeadLetteredAsync(int limit, CancellationToken cancellationToken)
    {
        LastDeadLetterLimit = limit;
        return ValueTask.FromResult(DeadLetters);
    }
}
