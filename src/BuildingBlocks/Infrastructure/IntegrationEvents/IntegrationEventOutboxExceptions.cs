namespace BuildingBlocks.Infrastructure.IntegrationEvents;

/// <summary>
/// Base for deterministic outbox dispatch failures — schema, deserialization, or identity mismatches — that cannot be
/// resolved by retry. The dispatcher treats subclasses as immediate dead-letter candidates so retry budget is not burned
/// on a broken row, and each concrete subclass maps to a distinct error code so operators can tell at a glance whether
/// the failing row is a code/assembly miss (<see cref="UnresolvableIntegrationEventTypeException"/>), a payload
/// corruption (<see cref="IntegrationEventDeserializationException"/>), or an id mismatch
/// (<see cref="IntegrationEventIdMismatchException"/>).
/// </summary>
public abstract class IntegrationEventOutboxDispatchException : Exception
{
    protected IntegrationEventOutboxDispatchException(
        string message,
        long messageId,
        string moduleKey,
        string eventType,
        Exception? innerException = null)
        : base(message, innerException)
    {
        MessageId = messageId;
        ModuleKey = moduleKey;
        EventType = eventType;
    }

    public long MessageId { get; }

    public string ModuleKey { get; }

    public string EventType { get; }
}

public sealed class UnresolvableIntegrationEventTypeException : IntegrationEventOutboxDispatchException
{
    public UnresolvableIntegrationEventTypeException(
        long messageId,
        string moduleKey,
        string eventType,
        Exception? innerException = null)
        : base(
            $"Outbox message {messageId} in module '{moduleKey}' references integration event type '{eventType}' which cannot be resolved in the current composition.",
            messageId,
            moduleKey,
            eventType,
            innerException)
    {
    }
}

public sealed class IntegrationEventDeserializationException : IntegrationEventOutboxDispatchException
{
    public IntegrationEventDeserializationException(
        long messageId,
        string moduleKey,
        string eventType,
        Exception? innerException = null)
        : base(
            $"Outbox message {messageId} in module '{moduleKey}' with stored type '{eventType}' failed to deserialize.",
            messageId,
            moduleKey,
            eventType,
            innerException)
    {
    }
}

public sealed class IntegrationEventIdMismatchException : IntegrationEventOutboxDispatchException
{
    public IntegrationEventIdMismatchException(
        long messageId,
        string moduleKey,
        string eventType,
        Guid storedEventId,
        Guid deserializedEventId)
        : base(
            $"Outbox message {messageId} in module '{moduleKey}' of type '{eventType}' deserialized event id '{deserializedEventId}' but the stored id is '{storedEventId}'.",
            messageId,
            moduleKey,
            eventType)
    {
        StoredEventId = storedEventId;
        DeserializedEventId = deserializedEventId;
    }

    public Guid StoredEventId { get; }

    public Guid DeserializedEventId { get; }
}
