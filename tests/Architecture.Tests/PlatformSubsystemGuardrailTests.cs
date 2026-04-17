namespace Architecture.Tests;

public sealed class PlatformSubsystemGuardrailTests
{
    [Fact]
    public void ModuleRuntimeSubsystemKeepsNamedGateGuardAndWorkerControlEntryPoints()
    {
        var gateSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Modules/ModuleExecutionGate.cs");
        var behaviorSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Dispatching/Behaviors/ModuleStateBehavior.cs");
        var endpointFilterSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Modules/ModuleStateEndpointFilter.cs");
        var integrationEventDispatcherSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Dispatching/IntegrationEventDispatcher.cs");
        var workerSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Modules/ModulePollingBackgroundService.cs");

        Assert.Contains("public interface IModuleExecutionGate", gateSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class ModuleExecutionGate", gateSource, StringComparison.Ordinal);
        Assert.Contains("IModuleStateGuard", gateSource, StringComparison.Ordinal);
        Assert.Contains("IModuleWorkLeaseManager", gateSource, StringComparison.Ordinal);
        Assert.Contains("TryEnterAsync", behaviorSource, StringComparison.Ordinal);
        Assert.Contains("TryEnterAsync", endpointFilterSource, StringComparison.Ordinal);
        Assert.Contains("TryEnterAsync", integrationEventDispatcherSource, StringComparison.Ordinal);
        Assert.Contains("public abstract class ModulePollingBackgroundService", workerSource, StringComparison.Ordinal);
        Assert.Contains("IModuleExecutionGate", workerSource, StringComparison.Ordinal);
        Assert.Contains("TryEnterAsync", workerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationDeliverySubsystemKeepsOneCanonicalStoreRegistrationAndPump()
    {
        var infrastructureDefaultsSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/ServiceCollectionExtensions.cs");
        var registrationSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/IntegrationEvents/IntegrationEventOutboxServiceCollectionExtensions.cs");
        var dispatcherSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/IntegrationEvents/IntegrationEventOutboxDispatcher.cs");
        var hostedServiceSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/IntegrationEvents/IntegrationEventOutboxHostedService.cs");

        Assert.Contains("public static IServiceCollection AddPostgresIntegrationEventOutbox", registrationSource, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IIntegrationEventOutboxStore>", registrationSource, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IDatabaseMigration>", registrationSource, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ICommandTransactionParticipant>", registrationSource, StringComparison.Ordinal);
        Assert.Contains("IModuleExecutionGate", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("TryEnterAsync", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("IIntegrationEventDispatcher", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("MarkDispatchedAsync", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("MarkFailedAsync", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("DeadLetterThreshold", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("IntegrationEventOutboxHostedService", infrastructureDefaultsSource, StringComparison.Ordinal);
        Assert.Contains("RunStorePumpAsync", hostedServiceSource, StringComparison.Ordinal);
        Assert.Contains("DispatchAvailableForModuleAsync", hostedServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaffoldAndGovernanceEngineKeepOneCanonicalStructuralPath()
    {
        var newModuleScript = RepositoryFiles.ReadAllText("scripts/New-Module.ps1");
        var validateGovernanceScript = RepositoryFiles.ReadAllText("scripts/Validate-Governance.ps1");

        Assert.Contains("templates/module/scaffold.contract.json", newModuleScript, StringComparison.Ordinal);
        Assert.Contains("Test-ModuleSpec -Spec $spec", newModuleScript, StringComparison.Ordinal);
        Assert.Contains("Add-GeneratedProjectsToSolution", newModuleScript, StringComparison.Ordinal);
        Assert.Contains("dotnet sln", newModuleScript, StringComparison.Ordinal);
        Assert.Contains("'templates/module/scaffold.contract.json'", validateGovernanceScript, StringComparison.Ordinal);
        Assert.Contains("'templates/module/module-spec.example.json'", validateGovernanceScript, StringComparison.Ordinal);
        Assert.Contains("'scripts/New-Module.ps1'", validateGovernanceScript, StringComparison.Ordinal);
        Assert.Contains("'scripts/Test-ModuleScaffold.ps1'", validateGovernanceScript, StringComparison.Ordinal);
        Assert.Contains("'scripts/Test-ModuleScaffoldFrontend.ps1'", validateGovernanceScript, StringComparison.Ordinal);
    }
}
