namespace BuildingBlocks.Application.Dispatching;

public static class IntegrationEventTypeNames
{
    public static string GetName(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return eventType.FullName
            ?? throw new InvalidOperationException(
                $"Integration event type '{eventType.AssemblyQualifiedName ?? eventType.Name}' must expose a stable full name.");
    }
}
