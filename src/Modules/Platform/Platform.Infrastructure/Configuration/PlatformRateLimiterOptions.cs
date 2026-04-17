namespace Platform.Infrastructure.Configuration;

public sealed class PlatformRateLimiterOptions
{
    public const string SectionName = "Modules:Platform:RateLimiter";

    public int ModuleStateMutationPermitLimit { get; init; } = 20;
}
