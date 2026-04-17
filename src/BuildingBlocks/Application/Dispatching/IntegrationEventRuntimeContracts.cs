using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Events;
using NodaTime;

namespace BuildingBlocks.Application.Dispatching;

public interface IModuleScopedIntegrationEventHandler<in TEvent> : IIntegrationEventHandler<TEvent>, IModuleScoped
    where TEvent : IIntegrationEvent
{
}

public interface IIntegrationEventDispatcher
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}

public enum IntegrationEventDeliveryDisposition
{
    Process = 0,
    SkipCompleted = 1,
    SkipInFlight = 2,
    SkipDeadLettered = 3
}

public readonly record struct IntegrationEventDeliveryContext(
    string ModuleKey,
    InboxConsumerName ConsumerName,
    Guid EventId,
    string EventType,
    Instant OccurredAt);

public readonly record struct IntegrationEventDeliveryDecision(IntegrationEventDeliveryDisposition Disposition)
{
    public static IntegrationEventDeliveryDecision Process { get; } = new(IntegrationEventDeliveryDisposition.Process);

    public static IntegrationEventDeliveryDecision SkipCompleted { get; } = new(IntegrationEventDeliveryDisposition.SkipCompleted);

    public static IntegrationEventDeliveryDecision SkipInFlight { get; } = new(IntegrationEventDeliveryDisposition.SkipInFlight);

    public static IntegrationEventDeliveryDecision SkipDeadLettered { get; } = new(IntegrationEventDeliveryDisposition.SkipDeadLettered);
}

public interface IIntegrationEventInboxStore
{
    string ModuleKey { get; }

    ValueTask<IntegrationEventDeliveryDecision> BeginAsync(
        IntegrationEventDeliveryContext context,
        Instant attemptedAt,
        CancellationToken cancellationToken);

    ValueTask MarkSucceededAsync(
        IntegrationEventDeliveryContext context,
        Instant completedAt,
        CancellationToken cancellationToken);

    ValueTask MarkFailedAsync(
        IntegrationEventDeliveryContext context,
        Instant failedAt,
        Error error,
        CancellationToken cancellationToken);
}

public sealed record IntegrationEventOutboxPublishRequest(
    string ModuleKey,
    IIntegrationEvent IntegrationEvent,
    Instant? AvailableAt = null,
    IReadOnlyDictionary<string, string>? Headers = null);

public sealed record IntegrationEventOutboxLeasedMessage(
    long MessageId,
    string ModuleKey,
    Guid EventId,
    string EventType,
    int EventVersion,
    string PayloadJson,
    IReadOnlyDictionary<string, string> Headers,
    Instant OccurredAt,
    Instant AvailableAt,
    int Attempts);

public interface IIntegrationEventOutboxPublisher
{
    ValueTask PublishAsync(IntegrationEventOutboxPublishRequest request, CancellationToken cancellationToken = default);
}

public interface IIntegrationEventOutboxStore
{
    string ModuleKey { get; }

    ValueTask EnqueueAsync(IntegrationEventOutboxPublishRequest request, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<IntegrationEventOutboxLeasedMessage>> LeaseAvailableAsync(
        int batchSize,
        Instant now,
        Instant leaseUntil,
        CancellationToken cancellationToken);

    ValueTask MarkDispatchedAsync(long messageId, Instant processedAt, CancellationToken cancellationToken);

    ValueTask MarkFailedAsync(
        long messageId,
        Instant failedAt,
        Instant nextAvailableAt,
        bool deadLettered,
        Error error,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<IntegrationEventOutboxDeadLetterEntry>> GetDeadLetteredAsync(
        int limit,
        CancellationToken cancellationToken);
}

public sealed record IntegrationEventOutboxDeadLetterEntry(
    Guid EventId,
    string EventType,
    string ModuleKey,
    DateTimeOffset DeadLetteredUtc,
    int Attempts,
    string? LastErrorCode,
    string? LastErrorMessage);

public interface IIntegrationEventOutboxDispatcher
{
    Task<int> DispatchAvailableAsync(CancellationToken cancellationToken = default);

    Task<int> DispatchAvailableForModuleAsync(string moduleKey, CancellationToken cancellationToken = default);
}
