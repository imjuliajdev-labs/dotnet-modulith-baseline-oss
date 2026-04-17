namespace BuildingBlocks.Infrastructure.IntegrationEvents;

public sealed class IntegrationEventOutboxProcessingOptions
{
    public int BatchSize { get; init; } = 25;

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan RetryBackoff { get; init; } = TimeSpan.FromSeconds(5);

    public int DeadLetterThreshold { get; init; } = 5;
}
