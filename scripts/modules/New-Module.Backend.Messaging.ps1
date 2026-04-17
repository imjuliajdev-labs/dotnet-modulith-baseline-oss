function New-EventContractContent {
    $introducedOn = (Get-Date -AsUTC).ToString('yyyy-MM-dd')
@"
using BuildingBlocks.Domain.Contracts;
using BuildingBlocks.Domain.Events;
using NodaTime;

namespace ${moduleName}.PublicContracts.Events;

[ContractLifecycle("${introducedOn}", ContractLifecycleStatus.Active)]
public sealed record ${moduleName}EventV1(Guid EventId, Instant OccurredAt, string EntityId) : IIntegrationEvent;
"@
}

function New-HttpContractV1Content {
    $introducedOn = (Get-Date -AsUTC).ToString('yyyy-MM-dd')
@"
using BuildingBlocks.Domain.Contracts;

namespace ${moduleName}.Api.Contracts;

[ContractLifecycle("${introducedOn}", ContractLifecycleStatus.Active)]
public sealed record ${moduleName}ContractV1(string Value);
"@
}

function New-IntegrationEventContractTestContent {
@"
using BuildingBlocks.Domain.Events;
using ${moduleName}.PublicContracts.Events;

namespace Architecture.Tests.ModuleCoverage.${moduleName}.Events;

public sealed class ${moduleName}IntegrationEventContractTests
{
    [Xunit.Fact]
    public void EventContractUsesTheGovernedPublicShape()
    {
        var eventType = typeof(${moduleName}EventV1);

        Xunit.Assert.Equal("${moduleName}.PublicContracts", eventType.Assembly.GetName().Name);
        Xunit.Assert.EndsWith("V1", eventType.Name, StringComparison.Ordinal);
        Xunit.Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(eventType));
        Xunit.Assert.NotNull(eventType.GetProperty(nameof(${moduleName}EventV1.EventId)));
        Xunit.Assert.NotNull(eventType.GetProperty(nameof(${moduleName}EventV1.OccurredAt)));
        Xunit.Assert.NotNull(eventType.GetProperty(nameof(${moduleName}EventV1.EntityId)));
    }
}
"@
}

function New-OutboxRegistrationContent {
@"
namespace ${moduleName}.Infrastructure.Outbox;

public static class ${moduleName}OutboxRegistration
{
}
"@
}

function New-OutboxIntegrationTestContent {
@"
using ${moduleName}.Infrastructure.Outbox;
using NodaTime;
using ${moduleName}.PublicContracts.Events;

namespace Integration.Tests.CapabilityCoverage.${moduleName}.Events;

public sealed class ${moduleName}OutboxIntegrationTests
{
    [Xunit.Fact]
    public void OutboxCapabilityShellAnchorsGovernedRuntimeSeams()
    {
        var integrationEvent = new ${moduleName}EventV1(Guid.NewGuid(), Instant.FromUtc(2026, 4, 4, 12, 0), "entity-1");

        Xunit.Assert.Equal("${moduleName}.PublicContracts", typeof(${moduleName}EventV1).Assembly.GetName().Name);
        Xunit.Assert.Equal("${moduleName}.PublicContracts.Events", typeof(${moduleName}EventV1).Namespace);
        Xunit.Assert.Equal("${moduleName}.Infrastructure", typeof(${moduleName}OutboxRegistration).Assembly.GetName().Name);
        Xunit.Assert.Equal("${moduleName}.Infrastructure.Outbox", typeof(${moduleName}OutboxRegistration).Namespace);
        Xunit.Assert.NotEqual(Guid.Empty, integrationEvent.EventId);
        Xunit.Assert.Equal("entity-1", integrationEvent.EntityId);
    }
}
"@
}

function New-IntegrationConsumerContent {
@"
using BuildingBlocks.Application.Modules;

namespace ${moduleName}.Application.Consumers;

public sealed class ${moduleName}IntegrationEventConsumer : IModuleScoped
{
    public string ModuleKey => "$moduleKey";
}
"@
}

function New-InboxStoreContent {
@"
namespace ${moduleName}.Infrastructure.Inbox;

public sealed class ${moduleName}InboxStore
{
}
"@
}

function New-ConsumerReplayIntegrationTestContent {
@"
using BuildingBlocks.Application.Modules;
using ${moduleName}.Application.Consumers;
using ${moduleName}.Infrastructure.Inbox;

namespace Integration.Tests.CapabilityCoverage.${moduleName}.Events;

public sealed class ${moduleName}ConsumerReplayIntegrationTests
{
    [Xunit.Fact]
    public void ConsumerReplayCapabilityShellAnchorsGovernedRuntimeSeams()
    {
        var consumer = new ${moduleName}IntegrationEventConsumer();
        var inboxStore = new ${moduleName}InboxStore();

        Xunit.Assert.IsAssignableFrom<IModuleScoped>(consumer);
        Xunit.Assert.Equal("${moduleName}.Application", typeof(${moduleName}IntegrationEventConsumer).Assembly.GetName().Name);
        Xunit.Assert.Equal("${moduleName}.Application.Consumers", typeof(${moduleName}IntegrationEventConsumer).Namespace);
        Xunit.Assert.Equal("${moduleName}.Infrastructure", typeof(${moduleName}InboxStore).Assembly.GetName().Name);
        Xunit.Assert.Equal("${moduleName}.Infrastructure.Inbox", typeof(${moduleName}InboxStore).Namespace);
        Xunit.Assert.Equal("$moduleKey", consumer.ModuleKey);
        Xunit.Assert.IsType<${moduleName}InboxStore>(inboxStore);
    }
}
"@
}

function New-SharedReadModelContractTestContent {
@"
using ${moduleName}.PublicContracts.Queries;

namespace Architecture.Tests.ModuleCoverage.${moduleName}.SharedQueries;

public sealed class ${moduleName}SharedQueryContractTests
{
    [Xunit.Fact]
    public void ReadModelLivesInPublicContractsQueries()
    {
        var contractType = typeof(${moduleName}ReadModel);

        Xunit.Assert.Equal("${moduleName}.PublicContracts", contractType.Assembly.GetName().Name);
        Xunit.Assert.Equal("${moduleName}.PublicContracts.Queries", contractType.Namespace);
        Xunit.Assert.NotNull(contractType.GetProperty(nameof(${moduleName}ReadModel.Value)));
    }
}
"@
}

function New-ProcessManagerContent {
@"
using BuildingBlocks.Application.Modules;

namespace ${moduleName}.Application.ProcessManagers;

public sealed class ${moduleName}ProcessManager : IModuleScoped
{
    public string ModuleKey => "$moduleKey";
}
"@
}

function New-ProcessManagerStateStoreContent {
@"
namespace ${moduleName}.Infrastructure.ProcessManagers;

public sealed class ${moduleName}ProcessManagerStateStore
{
}
"@
}

function New-ProcessManagerIntegrationTestContent {
@"
using BuildingBlocks.Application.Modules;
using ${moduleName}.Application.ProcessManagers;
using ${moduleName}.Infrastructure.ProcessManagers;

namespace Integration.Tests.CapabilityCoverage.${moduleName}.ProcessManagers;

public sealed class ${moduleName}ProcessManagerIntegrationTests
{
    [Xunit.Fact]
    public void ProcessManagerCapabilityShellAnchorsGovernedRuntimeSeams()
    {
        var processManager = new ${moduleName}ProcessManager();
        var stateStore = new ${moduleName}ProcessManagerStateStore();

        Xunit.Assert.IsAssignableFrom<IModuleScoped>(processManager);
        Xunit.Assert.Equal("${moduleName}.Application", typeof(${moduleName}ProcessManager).Assembly.GetName().Name);
        Xunit.Assert.Equal("${moduleName}.Application.ProcessManagers", typeof(${moduleName}ProcessManager).Namespace);
        Xunit.Assert.Equal("${moduleName}.Infrastructure", typeof(${moduleName}ProcessManagerStateStore).Assembly.GetName().Name);
        Xunit.Assert.Equal("${moduleName}.Infrastructure.ProcessManagers", typeof(${moduleName}ProcessManagerStateStore).Namespace);
        Xunit.Assert.Equal("$moduleKey", processManager.ModuleKey);
        Xunit.Assert.IsType<${moduleName}ProcessManagerStateStore>(stateStore);
    }
}
"@
}
