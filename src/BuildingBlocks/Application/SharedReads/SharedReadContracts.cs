namespace BuildingBlocks.Application.SharedReads;

public enum SharedReadFallbackBehavior
{
    Fail = 0,
    ReturnFallback = 1
}

public sealed record SharedReadPolicy(
    string Name,
    TimeSpan Timeout,
    string FreshnessExpectation,
    SharedReadFallbackBehavior FallbackBehavior);
