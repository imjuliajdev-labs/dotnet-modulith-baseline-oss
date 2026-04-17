namespace Admin.Infrastructure.Configuration;

public sealed class AdminInfrastructureOptions
{
    public const string SectionName = "Modules:Admin";

    public AdminProjectionRecoveryWorkerOptions ProjectionRecoveryWorker { get; init; } = new();
}

public sealed class AdminProjectionRecoveryWorkerOptions
{
    public int BatchSize { get; init; } = 25;

    public int PollIntervalMilliseconds { get; init; } = 500;

    public int StaleAfterMilliseconds { get; init; } = 10_000;
}
