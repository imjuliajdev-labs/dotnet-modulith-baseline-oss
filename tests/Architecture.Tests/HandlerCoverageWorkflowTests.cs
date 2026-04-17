namespace Architecture.Tests;

public sealed class HandlerCoverageWorkflowTests
{
    [Fact]
    public void ArchitectureTestsWorkflowPublishesTheHandlerCoverageArtifact()
    {
        var workflowLines = RepositoryFiles.ReadAllText(".github/workflows/ci.yml")
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);
        var jobBlock = GetJobBlock(workflowLines, "architecture-tests");
        var normalizedBlock = string.Join('\n', jobBlock);

        // After the local-gate-parity refactor, the architecture-tests job dispatches
        // the canonical gate id rather than inlining the dotnet command. The actual
        // command body lives in governance/ci-gates.json under the same id.
        Assert.Contains("./scripts/Invoke-CiGate.ps1 -Id architecture-tests", normalizedBlock, StringComparison.Ordinal);
        Assert.Contains("uses: actions/upload-artifact@v4", normalizedBlock, StringComparison.Ordinal);
        Assert.Contains("name: handler-coverage", normalizedBlock, StringComparison.Ordinal);
        Assert.Contains("path: artifacts/handler-coverage.json", normalizedBlock, StringComparison.Ordinal);

        var manifestText = RepositoryFiles.ReadAllText("governance/ci-gates.json");
        Assert.Contains("\"id\": \"architecture-tests\"", manifestText, StringComparison.Ordinal);

        var architectureRunnerScript = RepositoryFiles.ReadAllText("scripts/Invoke-ArchitectureTests.ps1");
        Assert.Contains("tests/Architecture.Tests/Architecture.Tests.csproj", architectureRunnerScript, StringComparison.Ordinal);
    }

    private static string[] GetJobBlock(string[] workflowLines, string jobId)
    {
        ArgumentNullException.ThrowIfNull(workflowLines);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var jobHeader = $"  {jobId}:";
        var startIndex = Array.IndexOf(workflowLines, jobHeader);
        Assert.True(startIndex >= 0, $"Expected CI workflow job '{jobId}'.");

        var jobBlock = new List<string>();
        for (var index = startIndex + 1; index < workflowLines.Length; index++)
        {
            var line = workflowLines[index];
            if (line.StartsWith("  ", StringComparison.Ordinal)
                && !line.StartsWith("    ", StringComparison.Ordinal)
                && line.EndsWith(':'))
            {
                break;
            }

            jobBlock.Add(line);
        }

        return jobBlock.ToArray();
    }
}
