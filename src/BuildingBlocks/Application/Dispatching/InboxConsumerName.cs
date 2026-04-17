namespace BuildingBlocks.Application.Dispatching;

/// <summary>
/// Canonical identity of an integration-event inbox consumer. Two handlers sharing the
/// same <see cref="InboxConsumerName"/> would collide on the inbox's
/// <c>(consumer_name, event_id)</c> primary key and silently share delivery state, so
/// construction is restricted to a single factory that derives the name from a handler
/// type. Test code that needs a synthetic name should use <see cref="ForTest"/>.
/// </summary>
public readonly record struct InboxConsumerName
{
    public const int MaxLength = 512;

    private InboxConsumerName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? string.Empty;

    /// <summary>
    /// Derives the canonical consumer name from an integration-event handler type. The
    /// returned name is the handler's assembly-qualified type name (FullName) — stable
    /// across releases as long as the type is not renamed, and already unique per CLR rules.
    /// </summary>
    public static InboxConsumerName FromHandlerType(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);

        var fullName = handlerType.FullName;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException(
                $"Handler type {handlerType.Name} does not expose a FullName and cannot be used as an inbox consumer.");
        }

        if (fullName.Length > MaxLength)
        {
            throw new InvalidOperationException(
                $"Handler type {fullName} exceeds the {MaxLength}-character inbox consumer name budget.");
        }

        return new InboxConsumerName(fullName);
    }

    /// <summary>
    /// Test-only factory for constructing a consumer name from an arbitrary string. Not
    /// callable from production code — the architecture test
    /// <c>InboxConsumerNameIsOnlyConstructedByDispatcher</c> enforces the restriction.
    /// </summary>
    public static InboxConsumerName ForTest(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"Inbox consumer name must be at most {MaxLength} characters.",
                nameof(value));
        }

        return new InboxConsumerName(value);
    }
}
