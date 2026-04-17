using System.Text.Json;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Domain.Time;
using BuildingBlocks.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Duration = NodaTime.Duration;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

public sealed class IntegrationEventOutboxDispatcher : IIntegrationEventOutboxDispatcher
{
    private readonly IClock _clock;
    private readonly IExceptionToErrorMapper _exceptionToErrorMapper;
    private readonly IModuleExecutionGate _moduleExecutionGate;
    private readonly JsonSerializerOptions _jsonOptions = StarterJsonSerializerOptions.Create();
    private readonly ILogger<IntegrationEventOutboxDispatcher> _logger;
    private readonly IntegrationEventOutboxProcessingOptions _options;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IReadOnlyCollection<IIntegrationEventOutboxStore> _stores;

    public IntegrationEventOutboxDispatcher(
        IEnumerable<IIntegrationEventOutboxStore> stores,
        IServiceScopeFactory serviceScopeFactory,
        IExceptionToErrorMapper exceptionToErrorMapper,
        IModuleExecutionGate moduleExecutionGate,
        IClock clock,
        IntegrationEventOutboxProcessingOptions options,
        ILogger<IntegrationEventOutboxDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(stores);

        _stores = stores.ToArray();
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _exceptionToErrorMapper = exceptionToErrorMapper ?? throw new ArgumentNullException(nameof(exceptionToErrorMapper));
        _moduleExecutionGate = moduleExecutionGate ?? throw new ArgumentNullException(nameof(moduleExecutionGate));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> DispatchAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (_stores.Count == 0)
        {
            return 0;
        }

        var now = _clock.GetCurrentInstant();
        var leaseUntil = now.Plus(Duration.FromTimeSpan(_options.LeaseDuration));

        var dispatchedCount = 0;
        foreach (var store in _stores)
        {
            dispatchedCount += await DispatchStoreAvailableAsync(store, now, leaseUntil, cancellationToken);
        }

        return dispatchedCount;
    }

    public async Task<int> DispatchAvailableForModuleAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        var store = _stores.SingleOrDefault(candidate => string.Equals(candidate.ModuleKey, moduleKey, StringComparison.Ordinal));
        if (store is null)
        {
            return 0;
        }

        var now = _clock.GetCurrentInstant();
        var leaseUntil = now.Plus(Duration.FromTimeSpan(_options.LeaseDuration));
        return await DispatchStoreAvailableAsync(store, now, leaseUntil, cancellationToken);
    }

    private async Task<int> DispatchStoreAvailableAsync(
        IIntegrationEventOutboxStore store,
        NodaTime.Instant now,
        NodaTime.Instant leaseUntil,
        CancellationToken cancellationToken)
    {
        await using var execution = await _moduleExecutionGate.TryEnterAsync(store.ModuleKey, cancellationToken);
        if (!execution.IsEntered)
        {
            return 0;
        }

        var dispatchedCount = 0;
        var leasedMessages = await store.LeaseAvailableAsync(_options.BatchSize, now, leaseUntil, cancellationToken);
        foreach (var leasedMessage in leasedMessages)
        {
            try
            {
                var integrationEvent = Deserialize(leasedMessage);
                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var integrationEventDispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
                await integrationEventDispatcher.PublishAsync(integrationEvent, cancellationToken);
                await store.MarkDispatchedAsync(leasedMessage.MessageId, _clock.GetCurrentInstant(), cancellationToken);
                dispatchedCount++;
            }
            catch (Exception exception)
            {
                var failedAt = _clock.GetCurrentInstant();
                var error = _exceptionToErrorMapper.Map(exception);
                // Deterministic failures (unresolvable type, deserialize error, id mismatch) will fail identically on
                // every retry. Burning the retry budget on them only wastes I/O and delays surfacing the problem to
                // operators, so they dead-letter on the first attempt regardless of DeadLetterThreshold.
                var deterministicFailure = exception is IntegrationEventOutboxDispatchException;
                var deadLettered = deterministicFailure || leasedMessage.Attempts >= _options.DeadLetterThreshold;
                var nextAvailableAt = deadLettered
                    ? failedAt
                    : failedAt.Plus(Duration.FromTimeSpan(_options.RetryBackoff));

                await store.MarkFailedAsync(
                    leasedMessage.MessageId,
                    failedAt,
                    nextAvailableAt,
                    deadLettered,
                    error,
                    cancellationToken);

                _logger.LogWarning(
                    exception,
                    "Integration-event outbox dispatch failed for module {ModuleKey} message {MessageId}. DeadLettered={DeadLettered}.",
                    leasedMessage.ModuleKey,
                    leasedMessage.MessageId,
                    deadLettered);
            }
        }

        return dispatchedCount;
    }

    private IIntegrationEvent Deserialize(IntegrationEventOutboxLeasedMessage leasedMessage)
    {
        var eventType = LoadedIntegrationEventTypeResolver.TryResolve(leasedMessage.EventType);
        if (eventType is null || !typeof(IIntegrationEvent).IsAssignableFrom(eventType))
        {
            throw new UnresolvableIntegrationEventTypeException(
                leasedMessage.MessageId,
                leasedMessage.ModuleKey,
                leasedMessage.EventType);
        }

        IIntegrationEvent? integrationEvent;
        try
        {
            integrationEvent = JsonSerializer.Deserialize(leasedMessage.PayloadJson, eventType, _jsonOptions) as IIntegrationEvent;
        }
        catch (JsonException jsonException)
        {
            throw new IntegrationEventDeserializationException(
                leasedMessage.MessageId,
                leasedMessage.ModuleKey,
                leasedMessage.EventType,
                jsonException);
        }

        if (integrationEvent is null)
        {
            throw new IntegrationEventDeserializationException(
                leasedMessage.MessageId,
                leasedMessage.ModuleKey,
                leasedMessage.EventType);
        }

        if (integrationEvent.EventId != leasedMessage.EventId)
        {
            throw new IntegrationEventIdMismatchException(
                leasedMessage.MessageId,
                leasedMessage.ModuleKey,
                leasedMessage.EventType,
                leasedMessage.EventId,
                integrationEvent.EventId);
        }

        return integrationEvent;
    }
}
