namespace Architecture.Tests;

public sealed class GovernanceAssetTests
{
    [Fact]
    public void StepOneGovernanceAssetsExist()
    {
        var requiredPaths = new[]
        {
            RepositoryFiles.PathFromRoot("README.md"),
            RepositoryFiles.PathFromRoot("AGENTS.md"),
            RepositoryFiles.PathFromRoot("docs", "BLUEPRINT.md"),
            RepositoryFiles.PathFromRoot("docs", "QUALITY_GATES.md"),
            RepositoryFiles.PathFromRoot("docs", "ADD_MODULE.md"),
            RepositoryFiles.PathFromRoot("docs", "RULE_TO_GATE_CATALOG.md"),
            RepositoryFiles.PathFromRoot("docs", "RULE_ENFORCEMENT_MAP.json"),
            RepositoryFiles.PathFromRoot("docs", "schema", "rule-enforcement-map.v1.schema.json"),
            RepositoryFiles.PathFromRoot("docs", "schema", "adopt-spec.v1.schema.json"),
            RepositoryFiles.PathFromRoot("docs", "adr", "README.md"),
            RepositoryFiles.PathFromRoot("waivers", "README.md"),
            RepositoryFiles.PathFromRoot("waivers", "active.waivers.json"),
            RepositoryFiles.PathFromRoot("waivers", "schema", "waiver-entry.v1.schema.json"),
            RepositoryFiles.PathFromRoot("waivers", "schema", "waiver-file.v1.schema.json"),
            RepositoryFiles.PathFromRoot("templates", "adoption", "adopt-spec.example.json"),
            RepositoryFiles.PathFromRoot("templates", "module", "scaffold.contract.json"),
            RepositoryFiles.PathFromRoot("templates", "module", "module-spec.example.json"),
            RepositoryFiles.PathFromRoot("prompts", "add-governed-module.md"),
            RepositoryFiles.PathFromRoot("prompts", "adopt-governed-baseline.md"),
            RepositoryFiles.PathFromRoot("scripts", "Validate-Governance.ps1"),
            RepositoryFiles.PathFromRoot("scripts", "Validate-OpenApiCompatibility.ps1"),
            RepositoryFiles.PathFromRoot("scripts", "Validate-DependencyPolicy.ps1"),
            RepositoryFiles.PathFromRoot("scripts", "Adopt-Baseline.ps1"),
            RepositoryFiles.PathFromRoot("scripts", "Start-Adoption.ps1"),
            RepositoryFiles.PathFromRoot("scripts", "New-Module.ps1"),
            RepositoryFiles.PathFromRoot("scripts", "Invoke-CiGate.ps1"),
            RepositoryFiles.PathFromRoot("scripts", "Invoke-LocalGates.ps1"),
            RepositoryFiles.PathFromRoot("scripts", "Test-ModuleScaffold.ps1"),
            RepositoryFiles.PathFromRoot("scripts", "Test-ModuleScaffoldFrontend.ps1"),
            RepositoryFiles.PathFromRoot("scripts", "Test-ApiHostMissingConfiguration.ps1"),
            RepositoryFiles.PathFromRoot("tests", "Architecture.Tests", "Approved", "BuildingBlocks.PublicSurface.approved.txt"),
            RepositoryFiles.PathFromRoot("policies", "dependency-allowlists", "nuget.allowed-packages.txt"),
            RepositoryFiles.PathFromRoot("policies", "dependency-allowlists", "pnpm.allowed-packages.txt"),
            RepositoryFiles.PathFromRoot("tests", "Architecture.Tests", "Architecture.Tests.csproj"),
            RepositoryFiles.PathFromRoot("governance", "ci-gates.json"),
            RepositoryFiles.PathFromRoot(".github", "workflows", "ci.yml")
        };

        foreach (var path in requiredPaths)
        {
            Assert.True(File.Exists(path), $"Required governance asset is missing: {path}");
        }
    }

    [Fact]
    public void ActiveWaiverFileStartsEmptyAndUsesTheCanonicalSchema()
    {
        using var document = RepositoryFiles.ReadJsonDocument("waivers", "active.waivers.json");
        var root = document.RootElement;

        Assert.Equal("./schema/waiver-file.v1.schema.json", root.GetProperty("$schema").GetString());
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(0, root.GetProperty("waivers").GetArrayLength());
    }

    [Fact]
    public void GovernedDocsMakeSharedAbstractionAdmissionRuleExplicit()
    {
        var blueprint = RepositoryFiles.ReadAllText("docs/BLUEPRINT.md");
        var qualityGates = RepositoryFiles.ReadAllText("docs/QUALITY_GATES.md");
        var agents = RepositoryFiles.ReadAllText("AGENTS.md");
        var ruleCatalog = RepositoryFiles.ReadAllText("docs/RULE_TO_GATE_CATALOG.md");

        Assert.Contains("removes more complexity than it introduces", blueprint, StringComparison.Ordinal);
        Assert.Contains("approved shared public surface", blueprint, StringComparison.Ordinal);
        Assert.Contains("approved shared public surface", qualityGates, StringComparison.Ordinal);
        Assert.Contains("approved shared public surface", agents, StringComparison.Ordinal);
        Assert.Contains("explicit admission", ruleCatalog, StringComparison.Ordinal);
    }

    [Fact]
    public void UserFacingGovernanceScriptsUseTheSharedScratchRootHelpers()
    {
        var governedScripts = new[]
        {
            "scripts/Generate-Contracts.ps1",
            "scripts/Test-ModuleScaffold.ps1",
            "scripts/Test-ModuleScaffoldFrontend.ps1",
            "scripts/Test-ApiHostBootstrap.ps1",
            "scripts/Test-ApiHostMissingMigrations.ps1",
        };

        var violations = new List<string>();

        foreach (var relativePath in governedScripts)
        {
            var source = RepositoryFiles.ReadAllText(relativePath);

            if (source.Contains("GetTempPath", StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: direct GetTempPath() usage is not supported; route scratch output through New-GovernanceScratchDirectory or New-GovernanceScratchFilePath.");
            }

            if (!source.Contains("New-GovernanceScratch", StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: must allocate scratch paths through the shared New-GovernanceScratch* helpers so every run lands under the repo scratch root.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void PlatformSubsystemAdrsDeclareCanonicalEntryPointsAndRegressionExpectations()
    {
        var dispatcherAdr = RepositoryFiles.ReadAllText("docs/adr/ADR-CUSTOM-DISPATCHER.md");
        var outboxAdr = RepositoryFiles.ReadAllText("docs/adr/ADR-CUSTOM-OUTBOX-INBOX.md");
        var moduleStateAdr = RepositoryFiles.ReadAllText("docs/adr/ADR-MODULE-STATE-DIRECT-DURABLE-READS.md");

        Assert.Contains("Canonical entry points", dispatcherAdr, StringComparison.Ordinal);
        Assert.Contains("Regression expectations", dispatcherAdr, StringComparison.Ordinal);
        Assert.Contains("Canonical entry points", outboxAdr, StringComparison.Ordinal);
        Assert.Contains("Regression expectations", outboxAdr, StringComparison.Ordinal);
        Assert.Contains("Canonical entry points", moduleStateAdr, StringComparison.Ordinal);
        Assert.Contains("Regression expectations", moduleStateAdr, StringComparison.Ordinal);
    }
}
