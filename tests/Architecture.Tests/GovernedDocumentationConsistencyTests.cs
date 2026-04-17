namespace Architecture.Tests;

public sealed class GovernedDocumentationConsistencyTests
{
    [Fact]
    public void ExecutableGovernanceChecksIncludeReadmeAndAgents()
    {
        var governanceScript = RepositoryFiles.ReadAllText("scripts/Validate-Governance.ps1");
        var governanceAssetTests = RepositoryFiles.ReadAllText("tests/Architecture.Tests/GovernanceAssetTests.cs");

        Assert.Contains("'README.md'", governanceScript, StringComparison.Ordinal);
        Assert.Contains("'AGENTS.md'", governanceScript, StringComparison.Ordinal);
        Assert.Contains("RepositoryFiles.PathFromRoot(\"README.md\")", governanceAssetTests, StringComparison.Ordinal);
        Assert.Contains("RepositoryFiles.PathFromRoot(\"AGENTS.md\")", governanceAssetTests, StringComparison.Ordinal);
    }

    [Fact]
    public void GovernedDocsUseApiModuleTerminologyConsistently()
    {
        var blueprint = RepositoryFiles.ReadAllText("docs/BLUEPRINT.md");
        var qualityGates = RepositoryFiles.ReadAllText("docs/QUALITY_GATES.md");
        var addModule = RepositoryFiles.ReadAllText("docs/ADD_MODULE.md");
        var readme = RepositoryFiles.ReadAllText("README.md");

        Assert.Contains("IApiModule", blueprint, StringComparison.Ordinal);
        Assert.Contains("IApiModule", qualityGates, StringComparison.Ordinal);
        Assert.Contains("IApiModule", addModule, StringComparison.Ordinal);
        Assert.Contains("module's `IApiModule` entry point", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("module's `IModule` entry point", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivePostureDocsDoNotReintroducePermissionSnapshotLanguage()
    {
        var activePostureDocs = new Dictionary<string, string>
        {
            ["docs/BLUEPRINT.md"] = RepositoryFiles.ReadAllText("docs/BLUEPRINT.md"),
            ["docs/QUALITY_GATES.md"] = RepositoryFiles.ReadAllText("docs/QUALITY_GATES.md"),
            ["README.md"] = RepositoryFiles.ReadAllText("README.md"),
            ["docs/adr/ADR-COOKIE-AUTH-OVER-JWT.md"] = RepositoryFiles.ReadAllText("docs/adr/ADR-COOKIE-AUTH-OVER-JWT.md")
        };

        foreach (var (_, text) in activePostureDocs)
        {
            Assert.DoesNotContain("permissionSnapshotVersion", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("permission snapshot", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void IdentityPostureAdrDoesNotClaimIdentityDomainWasRemoved()
    {
        var adr = RepositoryFiles.ReadAllText("docs/adr/ADR-IDENTITY-POSTURE.md");

        Assert.DoesNotContain(
            "Identity.Domain became empty",
            adr,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Identity.Domain` became empty",
            adr,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Identity.Domain is removed from the solution",
            adr,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Identity.Domain` is removed from the solution",
            adr,
            StringComparison.Ordinal);

        var identityDomainProject = RepositoryFiles.PathFromRoot(
            "src", "Modules", "Identity", "Identity.Domain", "Identity.Domain.csproj");
        Assert.True(
            File.Exists(identityDomainProject),
            "ADR-IDENTITY-POSTURE.md should not outlive the Identity.Domain project; if the project was removed, update the ADR instead of the test.");
    }
}
