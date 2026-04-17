namespace Architecture.Tests;

public sealed class TimeStrategyGuardrailTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTime.Today",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow",
        "TimeZoneInfo.Local",
        "ToLocalTime(",
        "DateTimeKind.Local",
        "Stopwatch.GetTimestamp",
        "Environment.TickCount",
    ];

    /// <summary>
    /// Test files that contain forbidden time tokens as string literals (rule definitions,
    /// forbidden-token lists, or documentation of what to look for). Adding a file here requires
    /// the tokens to be string literals only; any live code path that reads wall-clock time must
    /// instead flow through <see cref="NodaTime.SystemClock.Instance"/> or an injected clock.
    /// </summary>
    private static readonly HashSet<string> TestSourcesAllowedToMentionForbiddenTokens = new(StringComparer.Ordinal)
    {
        "tests/Architecture.Tests/TimeStrategyGuardrailTests.cs",
        "tests/Architecture.Tests/ApiEdgeGuardrailTests.cs",
    };

    [Fact]
    public void BackendSourceDoesNotUseServerLocalTimeApisOrDirectWallClockAccess()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src");
        var violations = new List<string>();

        foreach (var relativePath in sourceFiles)
        {
            var source = RepositoryFiles.ReadAllText(relativePath);

            foreach (var token in ForbiddenTokens)
            {
                if (source.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}: uses forbidden time API '{token}'. Route time access through NodaTime and the shared IClock abstraction.");
                }
            }

            if (source.Contains("SystemClock.Instance", StringComparison.Ordinal)
                && !string.Equals(relativePath, "src/BuildingBlocks/Infrastructure/Time/SystemClockAdapter.cs", StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: accesses SystemClock.Instance directly instead of the shared clock adapter.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void TestSourceDoesNotUseServerLocalTimeApisOrDirectWallClockAccess()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("tests");
        var violations = new List<string>();

        foreach (var relativePath in sourceFiles)
        {
            if (TestSourcesAllowedToMentionForbiddenTokens.Contains(relativePath))
            {
                continue;
            }

            var source = RepositoryFiles.ReadAllText(relativePath);

            foreach (var token in ForbiddenTokens)
            {
                if (source.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}: uses forbidden time API '{token}'. Tests must use a fake clock, a fixed Instant, or NodaTime.SystemClock.Instance.GetCurrentInstant().");
                }
            }
        }

        Assert.Empty(violations);
    }
}
