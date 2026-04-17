using System.Globalization;
using BuildingBlocks.Application;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Domain.Modules;
using BuildingBlocks.Domain.Time;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.Realtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Instant = NodaTime.Instant;
using Platform.Application.Auditing;
using Platform.Application.Authorization;
using Platform.Application.ModuleState;
using Platform.Domain.ModuleActivation;

namespace Platform.Infrastructure.Persistence;

public sealed class PostgresPlatformOperationalStore : IPlatformModuleStateStore, IAuditEventWriter, IPlatformAuditEventReader, IModuleWorkLeaseManager
{
    private const string MissingConnectionStringMessage = "Platform persistence requires ConnectionStrings:BaselineDatabase. No in-memory fallback is supported.";
    private const string TransitionFailedErrorCode = "platform.module_state.transition_failed";

    private readonly string? _connectionString;
    private readonly NpgsqlDataSource? _dataSource;
    private readonly IClock _clock;
    private readonly IDbContextFactory<PlatformPersistenceDbContext> _dbContextFactory;
    private readonly IReadOnlyDictionary<string, ModuleDescriptor> _moduleDescriptors;
    private readonly IBrowserRealtimeNotifier _realtimeNotifier;
    private readonly IReadOnlyDictionary<string, IReadOnlyCollection<IPlatformModuleTransitionParticipant>> _transitionParticipants;

    public PostgresPlatformOperationalStore(
        IPostgresDataSourceResolver dataSourceResolver,
        IClock clock,
        IDbContextFactory<PlatformPersistenceDbContext> dbContextFactory,
        IBrowserRealtimeNotifier realtimeNotifier,
        IEnumerable<IModule> modules,
        IEnumerable<IPlatformModuleTransitionParticipant> transitionParticipants)
    {
        ArgumentNullException.ThrowIfNull(dataSourceResolver);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _realtimeNotifier = realtimeNotifier ?? throw new ArgumentNullException(nameof(realtimeNotifier));
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(transitionParticipants);

        _connectionString = dataSourceResolver.GetConnectionString(PlatformPersistenceDefaults.ConnectionStringName);
        _dataSource = string.IsNullOrWhiteSpace(_connectionString)
            ? null
            : dataSourceResolver.GetRequiredDataSource(PlatformPersistenceDefaults.ConnectionStringName);

        var descriptors = modules
            .Select(static module => module.Descriptor)
            .ToArray();

        var duplicates = descriptors
            .GroupBy(static descriptor => descriptor.Key, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate module registrations were found for: {string.Join(", ", duplicates.OrderBy(static key => key, StringComparer.Ordinal))}.");
        }

        _moduleDescriptors = descriptors.ToDictionary(static descriptor => descriptor.Key, StringComparer.Ordinal);
        _transitionParticipants = transitionParticipants
            .GroupBy(static participant => participant.ModuleKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyCollection<IPlatformModuleTransitionParticipant>)group.ToArray(),
                StringComparer.Ordinal);
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        EnsureConnectionStringConfigured();

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureModuleRowsAsync(dbContext, cancellationToken);
    }

    internal async Task RecoverIncompleteTransitionsAsync(CancellationToken cancellationToken)
    {
        EnsureConnectionStringConfigured();

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var moduleKeys = await dbContext.ModuleStates
            .AsNoTracking()
            .Where(static state => state.RuntimeState == ModuleRuntimeState.Enabling || state.RuntimeState == ModuleRuntimeState.Disabling)
            .OrderBy(static state => state.ModuleKey)
            .Select(static state => state.ModuleKey)
            .ToArrayAsync(cancellationToken);

        foreach (var moduleKey in moduleKeys)
        {
            await ContinueIncompleteTransitionAsync(moduleKey, cancellationToken);
        }
    }

    public async ValueTask<ModuleStateSnapshot> GetRequiredStateAsync(string moduleKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var runtimeState = await dbContext.ModuleStates
            .AsNoTracking()
            .Where(state => state.ModuleKey == moduleKey)
            .Select(static state => state.RuntimeState)
            .SingleOrDefaultAsync(cancellationToken);

        if (!_moduleDescriptors.ContainsKey(moduleKey) || !await ModuleStateRowExistsAsync(dbContext, moduleKey, cancellationToken))
        {
            throw new InvalidOperationException($"No module state registration exists for module '{moduleKey}'.");
        }

        return new ModuleStateSnapshot(moduleKey, runtimeState);
    }

    public async ValueTask<IReadOnlyCollection<ManagedModuleState>> ListAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var states = await dbContext.ModuleStates
            .AsNoTracking()
            .ToDictionaryAsync(static state => state.ModuleKey, static state => state, StringComparer.Ordinal, cancellationToken);

        var changes = await dbContext.ModuleStateChanges
            .AsNoTracking()
            .OrderByDescending(static change => change.ChangedUtc)
            .ThenByDescending(static change => change.Id)
            .ToListAsync(cancellationToken);

        return _moduleDescriptors.Values
            .OrderBy(static descriptor => descriptor.Key, StringComparer.Ordinal)
            .Select(descriptor => CreateManagedState(
                descriptor,
                states.TryGetValue(descriptor.Key, out var state)
                    ? state
                    : CreateInitialRecord(descriptor, GetCurrentTimestamp()),
                changes.Where(change => string.Equals(change.ModuleKey, descriptor.Key, StringComparison.Ordinal))))
            .ToArray();
    }

    public async ValueTask<ModuleStateChangeResult> EnableAsync(
        string moduleKey,
        string? actorId,
        Instant changedUtc,
        CancellationToken cancellationToken)
    {
        return await ChangeStateAsync(moduleKey, ModuleDesiredState.Enabled, actorId, changedUtc, cancellationToken);
    }

    public async ValueTask<ModuleStateChangeResult> DisableAsync(
        string moduleKey,
        string? actorId,
        Instant changedUtc,
        CancellationToken cancellationToken)
    {
        return await ChangeStateAsync(moduleKey, ModuleDesiredState.Disabled, actorId, changedUtc, cancellationToken);
    }

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        dbContext.AuditEvents.Add(new PlatformAuditEventRecord
        {
            ModuleKey = auditEvent.ModuleKey,
            Action = auditEvent.Action,
            TargetType = auditEvent.TargetType,
            TargetId = auditEvent.TargetId,
            Outcome = auditEvent.Outcome,
            OccurredUtc = auditEvent.OccurredUtc,
            ActorId = NormalizeActorId(auditEvent.ActorId),
            CorrelationId = NormalizeCorrelationId(auditEvent.CorrelationId)
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<PlatformAuditEntry>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Audit query limit must be greater than zero.");
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.AuditEvents
            .AsNoTracking()
            .OrderByDescending(static auditEvent => auditEvent.OccurredUtc)
            .ThenByDescending(static auditEvent => auditEvent.Id)
            .Take(limit)
            .Select(static auditEvent => new PlatformAuditEntry(
                auditEvent.ModuleKey,
                auditEvent.Action,
                auditEvent.TargetType,
                auditEvent.TargetId,
                auditEvent.Outcome,
                auditEvent.OccurredUtc,
                auditEvent.ActorId,
                auditEvent.CorrelationId))
            .ToArrayAsync(cancellationToken);
    }

    public async ValueTask<CursorPagedResult<PlatformAuditEntry>> ListPagedAsync(int limit, string? afterCursor, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Audit query limit must be greater than zero.");
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<PlatformAuditEventRecord> query = dbContext.AuditEvents.AsNoTracking();

        var decoded = CursorEncoding.Decode(afterCursor);
        if (decoded.Status == CursorDecodeStatus.Valid
            && long.TryParse(decoded.SortValue, CultureInfo.InvariantCulture, out var lastOccurredTicks)
            && long.TryParse(decoded.Id, CultureInfo.InvariantCulture, out var lastId))
        {
            var lastOccurredUtc = Instant.FromUnixTimeTicks(lastOccurredTicks);
            query = query.Where(e =>
                e.OccurredUtc < lastOccurredUtc
                || (e.OccurredUtc == lastOccurredUtc && e.Id < lastId));
        }

        var records = await query
            .OrderByDescending(static e => e.OccurredUtc)
            .ThenByDescending(static e => e.Id)
            .Take(limit + 1)
            .Select(static e => new { e.Id, e.ModuleKey, e.Action, e.TargetType, e.TargetId, e.Outcome, e.OccurredUtc, e.ActorId, e.CorrelationId })
            .ToArrayAsync(cancellationToken);

        var hasMore = records.Length > limit;
        var page = hasMore ? records[..limit] : records;
        var items = page.Select(e => new PlatformAuditEntry(
            e.ModuleKey, e.Action, e.TargetType, e.TargetId, e.Outcome, e.OccurredUtc, e.ActorId, e.CorrelationId)).ToArray();

        string? nextCursor = null;
        if (hasMore && page.Length > 0)
        {
            var last = page[^1];
            nextCursor = CursorEncoding.Encode(
                last.OccurredUtc.ToUnixTimeTicks().ToString(CultureInfo.InvariantCulture),
                last.Id.ToString(CultureInfo.InvariantCulture));
        }

        return new CursorPagedResult<PlatformAuditEntry>(items, nextCursor);
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(string moduleKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        return await PostgresAdvisoryLock.AcquireSharedAsync(
            GetRequiredDataSource(),
            $"platform.module-work:{moduleKey}",
            cancellationToken);
    }

    private async Task<ModuleStateChangeResult> ChangeStateAsync(
        string moduleKey,
        ModuleDesiredState targetState,
        string? actorId,
        Instant changedUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        if (!_moduleDescriptors.TryGetValue(moduleKey, out var descriptor))
        {
            return new ModuleStateChangeResult(ModuleStateChangeStatus.UnknownModule, null);
        }

        // Fast path: disablement refusal does not require loading state.
        if (targetState == ModuleDesiredState.Disabled && !descriptor.CanBeDisabled)
        {
            return new ModuleStateChangeResult(
                ModuleStateChangeStatus.NotDisableable,
                await BuildManagedStateAsync(descriptor, cancellationToken));
        }

        await using var advisoryLock = await AcquireModuleLockAsync(moduleKey, cancellationToken);
        await ContinueIncompleteTransitionCoreAsync(descriptor, cancellationToken);

        var aggregate = await LoadAggregateAsync(moduleKey, cancellationToken);

        if (aggregate.IsInStableTargetState(targetState))
        {
            return new ModuleStateChangeResult(
                targetState == ModuleDesiredState.Enabled
                    ? ModuleStateChangeStatus.AlreadyEnabled
                    : ModuleStateChangeStatus.AlreadyDisabled,
                await BuildManagedStateAsync(descriptor, cancellationToken));
        }

        var transitionId = Guid.NewGuid();
        var normalizedActorId = NormalizeActorId(actorId);

        aggregate.BeginTransition(targetState, descriptor.CanBeDisabled, transitionId, changedUtc, normalizedActorId);
        await PersistAggregateAsync(descriptor, aggregate, cancellationToken);

        try
        {
            await RunTransitionParticipantsAsync(descriptor.Key, aggregate.RuntimeState, cancellationToken);

            if (aggregate.RuntimeState == ModuleRuntimeState.Disabling)
            {
                await using var drainLock = await AcquireModuleWorkDrainLockAsync(descriptor.Key, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            aggregate.FailTransition(
                GetCurrentTimestamp(),
                normalizedActorId,
                TransitionFailedErrorCode,
                NormalizeErrorDetail(exception.Message));
            await PersistAggregateAsync(descriptor, aggregate, cancellationToken);

            return new ModuleStateChangeResult(
                ModuleStateChangeStatus.TransitionFailed,
                await BuildManagedStateAsync(descriptor, cancellationToken));
        }

        aggregate.CompleteTransition(GetCurrentTimestamp(), normalizedActorId);
        await PersistAggregateAsync(descriptor, aggregate, cancellationToken);

        var finalState = await BuildManagedStateAsync(descriptor, cancellationToken);
        await _realtimeNotifier.PublishToRolesAsync(
            [BuildingBlocks.Application.Authorization.IdentityRoles.Admin, BuildingBlocks.Application.Authorization.IdentityRoles.Machine],
            PlatformRealtimeChannels.ModuleStateChanged,
            PlatformModuleStateChangedNotification.From(finalState),
            cancellationToken);

        return new ModuleStateChangeResult(ModuleStateChangeStatus.Success, finalState);
    }

    private async Task<ManagedModuleState> BuildManagedStateAsync(
        ModuleDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var state = await dbContext.ModuleStates
            .AsNoTracking()
            .SingleAsync(record => record.ModuleKey == descriptor.Key, cancellationToken);

        var changes = await dbContext.ModuleStateChanges
            .AsNoTracking()
            .Where(change => change.ModuleKey == descriptor.Key)
            .OrderByDescending(static change => change.ChangedUtc)
            .ThenByDescending(static change => change.Id)
            .ToArrayAsync(cancellationToken);

        return CreateManagedState(descriptor, state, changes);
    }

    private async Task EnsureModuleRowsAsync(PlatformPersistenceDbContext dbContext, CancellationToken cancellationToken)
    {
        var existingModuleKeys = await dbContext.ModuleStates
            .AsNoTracking()
            .Select(static state => state.ModuleKey)
            .ToArrayAsync(cancellationToken);

        var changedUtc = GetCurrentTimestamp();
        var missingModuleStates = _moduleDescriptors.Values
            .Where(descriptor => !existingModuleKeys.Contains(descriptor.Key, StringComparer.Ordinal))
            .Select(descriptor => CreateInitialRecord(descriptor, changedUtc))
            .ToArray();

        if (missingModuleStates.Length == 0)
        {
            return;
        }

        dbContext.ModuleStates.AddRange(missingModuleStates);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private async Task ContinueIncompleteTransitionAsync(string moduleKey, CancellationToken cancellationToken)
    {
        await using var advisoryLock = await AcquireModuleLockAsync(moduleKey, cancellationToken);
        await ContinueIncompleteTransitionCoreAsync(GetRequiredDescriptor(moduleKey), cancellationToken);
    }

    private async Task<bool> ModuleStateRowExistsAsync(
        PlatformPersistenceDbContext dbContext,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        return await dbContext.ModuleStates
            .AsNoTracking()
            .AnyAsync(state => state.ModuleKey == moduleKey, cancellationToken);
    }

    private async Task ContinueIncompleteTransitionCoreAsync(ModuleDescriptor descriptor, CancellationToken cancellationToken)
    {
        var aggregate = await LoadAggregateAsync(descriptor.Key, cancellationToken);
        if (!aggregate.IsInTransition)
        {
            return;
        }

        var previousActorId = aggregate.UpdatedByActorId;

        try
        {
            await RunTransitionParticipantsAsync(descriptor.Key, aggregate.RuntimeState, cancellationToken);
        }
        catch (Exception exception)
        {
            aggregate.FailTransition(
                GetCurrentTimestamp(),
                previousActorId,
                TransitionFailedErrorCode,
                NormalizeErrorDetail(exception.Message));
            await PersistAggregateAsync(descriptor, aggregate, cancellationToken);

            throw new InvalidOperationException(
                $"Recovery of module '{descriptor.Key}' could not complete the interrupted transition.",
                exception);
        }

        aggregate.CompleteTransition(GetCurrentTimestamp(), previousActorId);
        await PersistAggregateAsync(descriptor, aggregate, cancellationToken);
    }

    private async Task<ModuleActivationState> LoadAggregateAsync(string moduleKey, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await dbContext.ModuleStates
            .AsNoTracking()
            .SingleAsync(state => state.ModuleKey == moduleKey, cancellationToken);

        return ModuleActivationState.Rehydrate(
            record.ModuleKey,
            record.DesiredState,
            record.RuntimeState,
            record.Version,
            record.TransitionId,
            record.UpdatedUtc,
            record.UpdatedByActorId,
            record.LastErrorCode,
            record.LastErrorDetail);
    }

    private async Task PersistAggregateAsync(
        ModuleDescriptor descriptor,
        ModuleActivationState aggregate,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var record = await dbContext.ModuleStates.SingleAsync(state => state.ModuleKey == descriptor.Key, cancellationToken);

        // The aggregate already bumped Version for the current mutation; the database row must still hold the pre-bump version.
        var expectedDbVersion = aggregate.Version - 1;
        if (record.Version != expectedDbVersion)
        {
            throw new InvalidOperationException($"A concurrent module-state transition was detected for module '{descriptor.Key}'.");
        }

        var beforeState = record.RuntimeState;
        record.DesiredState = aggregate.DesiredState;
        record.RuntimeState = aggregate.RuntimeState;
        record.Version = aggregate.Version;
        record.TransitionId = aggregate.TransitionId;
        record.UpdatedUtc = aggregate.UpdatedUtc;
        record.UpdatedByActorId = aggregate.UpdatedByActorId;
        record.LastErrorCode = aggregate.LastErrorCode;
        record.LastErrorDetail = aggregate.LastErrorDetail;

        dbContext.ModuleStateChanges.Add(new PlatformModuleStateChangeRecord
        {
            ModuleKey = descriptor.Key,
            BeforeState = beforeState,
            AfterState = aggregate.RuntimeState,
            ChangedUtc = aggregate.UpdatedUtc,
            ActorId = aggregate.UpdatedByActorId,
            TransitionId = aggregate.TransitionId
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Instant GetCurrentTimestamp()
    {
        return _clock.GetCurrentInstant();
    }

    private async Task RunTransitionParticipantsAsync(
        string moduleKey,
        ModuleRuntimeState runtimeState,
        CancellationToken cancellationToken)
    {
        if (!_transitionParticipants.TryGetValue(moduleKey, out var participants))
        {
            return;
        }

        foreach (var participant in participants)
        {
            if (runtimeState == ModuleRuntimeState.Enabling)
            {
                await participant.OnEnablingAsync(cancellationToken);
            }
            else if (runtimeState == ModuleRuntimeState.Disabling)
            {
                await participant.OnDisablingAsync(cancellationToken);
            }
        }
    }

    private async Task<IAsyncDisposable> AcquireModuleLockAsync(string moduleKey, CancellationToken cancellationToken)
    {
        return await PostgresAdvisoryLock.AcquireAsync(
            GetRequiredDataSource(),
            $"platform.module-state:{moduleKey}",
            cancellationToken);
    }

    private async Task<IAsyncDisposable> AcquireModuleWorkDrainLockAsync(string moduleKey, CancellationToken cancellationToken)
    {
        return await PostgresAdvisoryLock.AcquireAsync(
            GetRequiredDataSource(),
            $"platform.module-work:{moduleKey}",
            cancellationToken);
    }

    private string EnsureConnectionStringConfigured()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException(MissingConnectionStringMessage);
        }

        return _connectionString;
    }

    private NpgsqlDataSource GetRequiredDataSource()
    {
        if (_dataSource is null)
        {
            throw new InvalidOperationException(MissingConnectionStringMessage);
        }

        return _dataSource;
    }

    private ModuleDescriptor GetRequiredDescriptor(string moduleKey)
    {
        if (_moduleDescriptors.TryGetValue(moduleKey, out var descriptor))
        {
            return descriptor;
        }

        throw new InvalidOperationException($"No module state registration exists for module '{moduleKey}'.");
    }

    private static ManagedModuleState CreateManagedState(
        ModuleDescriptor descriptor,
        PlatformModuleStateRecord state,
        IEnumerable<PlatformModuleStateChangeRecord> changes)
    {
        return new ManagedModuleState(
            descriptor.Key,
            descriptor.DisplayName,
            descriptor.RoutePrefix,
            descriptor.DefaultEnabled,
            descriptor.CanBeDisabled,
            state.DesiredState,
            state.RuntimeState,
            state.Version,
            state.TransitionId,
            state.UpdatedUtc,
            state.UpdatedByActorId,
            state.LastErrorCode,
            state.LastErrorDetail,
            changes.Select(static change => new ManagedModuleStateChange(
                change.BeforeState,
                change.AfterState,
                change.ChangedUtc,
                change.ActorId,
                change.TransitionId)).ToArray());
    }

    private static PlatformModuleStateRecord CreateInitialRecord(ModuleDescriptor descriptor, Instant changedUtc)
    {
        var initial = ModuleActivationState.Initial(descriptor.Key, descriptor.DefaultEnabled, changedUtc);

        return new PlatformModuleStateRecord
        {
            ModuleKey = initial.ModuleKey,
            DesiredState = initial.DesiredState,
            RuntimeState = initial.RuntimeState,
            Version = initial.Version,
            TransitionId = initial.TransitionId,
            UpdatedUtc = initial.UpdatedUtc,
            UpdatedByActorId = initial.UpdatedByActorId,
            LastErrorCode = initial.LastErrorCode,
            LastErrorDetail = initial.LastErrorDetail
        };
    }

    private static string? NormalizeActorId(string? actorId)
    {
        return string.IsNullOrWhiteSpace(actorId)
            ? null
            : actorId.Trim();
    }

    private static string? NormalizeErrorDetail(string? errorDetail)
    {
        if (string.IsNullOrWhiteSpace(errorDetail))
        {
            return null;
        }

        var trimmed = errorDetail.Trim();
        return trimmed.Length <= 1024
            ? trimmed
            : trimmed[..1024];
    }

    private static string? NormalizeCorrelationId(string? correlationId)
    {
        return string.IsNullOrWhiteSpace(correlationId)
            ? null
            : correlationId.Trim();
    }
}
