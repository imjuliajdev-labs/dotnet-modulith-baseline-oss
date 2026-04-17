namespace Architecture.Tests;

public sealed class ScaffoldContractTests
{
    [Fact]
    public void ScaffoldContractUsesTheGovernedProjectShape()
    {
        var contract = RepositoryFiles.ReadScaffoldContract();
        var expectedSuffixes = new[]
        {
            ".Api",
            ".Application",
            ".Domain",
            ".Infrastructure",
            ".PublicContracts"
        };

        Assert.Equal(expectedSuffixes, contract.RequiredProjectSuffixes);
    }

    [Fact]
    public void ScaffoldContractCoversStepOneCapabilityPlaceholders()
    {
        var contract = RepositoryFiles.ReadScaffoldContract();
        var expectedCapabilities = new[]
        {
            "hasOperatorManagedSettings",
            "publishesIntegrationEvents",
            "consumesIntegrationEvents",
            "exposesSynchronousReadContracts",
            "needsProcessManager",
            "hasOptedInIdempotentCommands",
            "hasBackgroundWorkers",
            "introducesExternallyConsumedHttpContracts",
            "exposesMachineConsumableEndpoints",
            "emitsBrowserRealtimeNotifications",
            "requiresRecentAuthStepUp",
            "hasFrontendSurface"
        };

        Assert.Equal(expectedCapabilities.OrderBy(value => value), contract.OptionalPathsByCapability.Keys.OrderBy(value => value));
    }

    [Fact]
    public void ScaffoldContractIncludesLiveProjectShellsAndTestStubs()
    {
        var contract = RepositoryFiles.ReadScaffoldContract();
        var requiredPaths = contract.RequiredPaths;

        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Api/{ModuleName}.Api.csproj", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Api/{ModuleName}Module.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Application/{ModuleName}AssemblyMarker.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Domain/{ModuleName}AssemblyMarker.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Infrastructure/{ModuleName}AssemblyMarker.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.PublicContracts/{ModuleName}AssemblyMarker.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Infrastructure/{ModuleName}InfrastructureServiceCollectionExtensions.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Infrastructure/Persistence/{ModuleName}PersistenceDefaults.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Infrastructure/Persistence/{ModuleName}PersistenceDbContext.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Infrastructure/Persistence/{ModuleName}EntityTypeConfigurationExample.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Infrastructure/Persistence/{ModuleName}PersistenceOptionsExtensions.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Infrastructure/Persistence/{ModuleName}PersistenceDbContextFactory.cs", requiredPaths);
        Assert.Contains("src/Modules/{ModuleName}/{ModuleName}.Infrastructure/Persistence/{ModuleName}DatabaseMigration.cs", requiredPaths);
        Assert.Contains("tests/Architecture.Tests/Modules/{ModuleName}/{ModuleName}ArchitectureTests.cs", requiredPaths);
        Assert.Contains("tests/Architecture.Tests/Modules/{ModuleName}/Configuration/{ModuleName}ConfigurationGovernanceTests.cs", requiredPaths);
        Assert.Contains("tests/Integration.Tests/Modules/{ModuleName}/{ModuleName}BootstrapManifestTests.cs", requiredPaths);
        Assert.Contains("tests/Module.UnitTests/{ModuleName}/{ModuleName}ModuleDescriptorTests.cs", requiredPaths);
    }

    [Fact]
    public void ScaffoldContractRequiresSharedContractCompatibilityTestStubsForRelevantCapabilities()
    {
        var contract = RepositoryFiles.ReadScaffoldContract();

        Assert.Contains(
            "src/Modules/{ModuleName}/{ModuleName}.Application/Settings/{ModuleName}SettingsContracts.cs",
            contract.OptionalPathsByCapability["hasOperatorManagedSettings"]);
        Assert.Contains(
            "src/Modules/{ModuleName}/{ModuleName}.Api/{ModuleName}SettingsHttpModels.cs",
            contract.OptionalPathsByCapability["hasOperatorManagedSettings"]);
        Assert.Contains(
            "tests/Integration.Tests/Modules/{ModuleName}/Configuration/{ModuleName}SettingsIntegrationTests.cs",
            contract.OptionalPathsByCapability["hasOperatorManagedSettings"]);
        Assert.Contains(
            "tests/Architecture.Tests/Modules/{ModuleName}/SharedQueries/{ModuleName}SharedQueryContractTests.cs",
            contract.OptionalPathsByCapability["exposesSynchronousReadContracts"]);
        Assert.Contains(
            "tests/Architecture.Tests/Modules/{ModuleName}/Events/{ModuleName}IntegrationEventContractTests.cs",
            contract.OptionalPathsByCapability["publishesIntegrationEvents"]);
        Assert.Contains(
            "src/Modules/{ModuleName}/{ModuleName}.Infrastructure/Outbox/{ModuleName}OutboxRegistration.cs",
            contract.OptionalPathsByCapability["publishesIntegrationEvents"]);
        Assert.Contains(
            "tests/Integration.Tests/Modules/{ModuleName}/Events/{ModuleName}OutboxIntegrationTests.cs",
            contract.OptionalPathsByCapability["publishesIntegrationEvents"]);
        Assert.Contains(
            "tests/Integration.Tests/Modules/{ModuleName}/Events/{ModuleName}ConsumerReplayIntegrationTests.cs",
            contract.OptionalPathsByCapability["consumesIntegrationEvents"]);
        Assert.Contains(
            "src/Modules/{ModuleName}/{ModuleName}.Infrastructure/ProcessManagers/{ModuleName}ProcessManagerStateStore.cs",
            contract.OptionalPathsByCapability["needsProcessManager"]);
        Assert.Contains(
            "tests/Integration.Tests/Modules/{ModuleName}/ProcessManagers/{ModuleName}ProcessManagerIntegrationTests.cs",
            contract.OptionalPathsByCapability["needsProcessManager"]);
        Assert.Contains(
            "src/Modules/{ModuleName}/{ModuleName}.Application/Authorization/{ModuleName}RecentAuthenticationPolicy.cs",
            contract.OptionalPathsByCapability["requiresRecentAuthStepUp"]);
        Assert.Contains(
            "tests/Architecture.Tests/Modules/{ModuleName}/Authorization/{ModuleName}RecentAuthenticationPolicyTests.cs",
            contract.OptionalPathsByCapability["requiresRecentAuthStepUp"]);
    }

    [Fact]
    public void ScaffoldContractIncludesConditionalFrontendSettingsApiShellForModulesWithUiAndManagedSettings()
    {
        var contract = RepositoryFiles.ReadScaffoldContract();

        var conditionalGroup = Assert.Single(contract.ConditionalPaths);
        Assert.Equal(new[] { "hasOperatorManagedSettings", "hasFrontendSurface" }, conditionalGroup.AllOf);
        Assert.Contains("web/src/features/{ModuleKey}/{ModuleName}SettingsTags.ts", conditionalGroup.Paths);
        Assert.Contains("web/src/features/{ModuleKey}/{ModuleName}SettingsApi.ts", conditionalGroup.Paths);
        Assert.Contains("web/src/features/{ModuleKey}/{ModuleName}SettingsProjection.ts", conditionalGroup.Paths);
        Assert.Contains("web/tests/unit/{ModuleKey}-settings-api.test.ts", conditionalGroup.Paths);
        Assert.Contains("web/tests/unit/{ModuleKey}-settings-tags.test.ts", conditionalGroup.Paths);
        Assert.Contains("web/tests/unit/{ModuleKey}-feature.test.tsx", conditionalGroup.Paths);
        Assert.Contains("web/tests/e2e/{ModuleKey}.spec.ts", conditionalGroup.Paths);
    }

    [Fact]
    public void ScaffoldEngineScriptsStayAnchoredToTheGovernedContractAndValidationPath()
    {
        var newModuleScript = RepositoryFiles.ReadAllText("scripts/New-Module.ps1");
        var validateGovernanceScript = RepositoryFiles.ReadAllText("scripts/Validate-Governance.ps1");

        Assert.Contains("templates/module/scaffold.contract.json", newModuleScript, StringComparison.Ordinal);
        Assert.Contains("Get-ExpectedScaffoldPaths -Contract $contract -Spec $spec", newModuleScript, StringComparison.Ordinal);
        Assert.Contains("Add-GeneratedProjectsToSolution", newModuleScript, StringComparison.Ordinal);
        Assert.Contains("'templates/module/scaffold.contract.json'", validateGovernanceScript, StringComparison.Ordinal);
        Assert.Contains("'scripts/Test-ModuleScaffold.ps1'", validateGovernanceScript, StringComparison.Ordinal);
        Assert.Contains("'scripts/Test-ModuleScaffoldFrontend.ps1'", validateGovernanceScript, StringComparison.Ordinal);
    }
}
