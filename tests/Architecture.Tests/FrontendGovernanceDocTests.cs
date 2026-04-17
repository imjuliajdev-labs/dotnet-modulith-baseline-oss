namespace Architecture.Tests;

public sealed class FrontendGovernanceDocTests
{
    [Fact]
    public void FrontendGovernanceDocMentionsEveryFrontendBlueprintId()
    {
        var frontendGovernance = RepositoryFiles.ReadAllText("docs/governance/FRONTEND_GOVERNANCE.md");

        Assert.Contains("BP-010", frontendGovernance, StringComparison.Ordinal);
        Assert.Contains("BP-017", frontendGovernance, StringComparison.Ordinal);
        Assert.Contains("BP-036", frontendGovernance, StringComparison.Ordinal);
    }
}
