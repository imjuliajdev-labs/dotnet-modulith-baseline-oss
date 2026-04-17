using BuildingBlocks.Application;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Domain.Modules;
using NodaTime;
using Platform.Application.Auditing;
using Platform.Application.Bootstrap;
using Platform.Application.Health;
using Platform.Application.ModuleState;
using Platform.Application.Outbox;
using Platform.Domain.ModuleActivation;

namespace Module.UnitTests.Platform;

public sealed class GetBootstrapManifestQueryHandlerTests
{
    [Fact]
    public async Task Handle_sorts_modules_by_key_and_maps_their_descriptors()
    {
        var modules = new IModule[]
        {
            new FakeModule(new ModuleDescriptor("sample", "Sample", "/sample", "sample", "sample", true, true)),
            new FakeModule(new ModuleDescriptor("admin", "Admin", "/admin", "admin", "admin", true, true))
        };
        var handler = new GetBootstrapManifestQueryHandler(modules);

        var result = await handler.Handle(new GetBootstrapManifestQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Modules.ToArray();
        Assert.Equal(new[] { "admin", "sample" }, items.Select(static item => item.Key).ToArray());
        Assert.Equal("/admin", items[0].RoutePrefix);
        Assert.Equal("admin", items[0].ModuleNamespace);
    }
}

public sealed class GetMachineBootstrapManifestQueryHandlerTests
{
    [Fact]
    public async Task Handle_sorts_modules_by_key_and_maps_their_descriptors()
    {
        var modules = new IModule[]
        {
            new FakeModule(new ModuleDescriptor("knowledge", "Knowledge", "/knowledge", "knowledge", "knowledge", true, true)),
            new FakeModule(new ModuleDescriptor("blog", "Blog", "/blog", "blog", "blog", true, true))
        };
        var handler = new GetMachineBootstrapManifestQueryHandler(modules);

        var result = await handler.Handle(new GetMachineBootstrapManifestQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "blog", "knowledge" }, result.Value!.Modules.Select(static item => item.Key).ToArray());
    }
}

public sealed class GetOperationalHealthSummaryQueryHandlerTests
{
    [Fact]
    public async Task Handle_derives_module_and_overall_health_from_runtime_state()
    {
        var store = new RecordingPlatformModuleStateStore
        {
            States =
            [
                PlatformTestData.CreateModuleState("platform", "Platform", canBeDisabled: false, ModuleDesiredState.Enabled, ModuleRuntimeState.Enabled),
                PlatformTestData.CreateModuleState("blog", "Blog", canBeDisabled: true, ModuleDesiredState.Disabled, ModuleRuntimeState.Disabled),
                PlatformTestData.CreateModuleState("admin", "Admin", canBeDisabled: true, ModuleDesiredState.Enabled, ModuleRuntimeState.Disabling)
            ]
        };
        var handler = new GetOperationalHealthSummaryQueryHandler(store);

        var result = await handler.Handle(new GetOperationalHealthSummaryQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var summary = result.Value!;
        Assert.Equal(OperationalHealthStatus.Degraded, summary.OverallStatus);
        Assert.Equal(3, summary.TotalModules);
        Assert.Equal(1, summary.EnabledModules);
        Assert.Equal(1, summary.DisabledModules);
        Assert.Equal(
            [OperationalHealthStatus.Healthy, OperationalHealthStatus.Degraded, OperationalHealthStatus.Degraded],
            summary.Modules.Select(static module => module.Status).ToArray());
    }
}

public sealed class GetModuleStatesQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_the_managed_module_states_from_the_store()
    {
        var state = PlatformTestData.CreateModuleState("blog", "Blog", canBeDisabled: true, ModuleDesiredState.Enabled, ModuleRuntimeState.Enabled);
        var store = new RecordingPlatformModuleStateStore
        {
            States = [state]
        };
        var handler = new GetModuleStatesQueryHandler(store);

        var result = await handler.Handle(new GetModuleStatesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([state], result.Value!.Modules);
    }
}

public sealed class GetOutboxDeadLettersQueryHandlerTests
{
    [Fact]
    public async Task Handle_aggregates_stores_sorts_descending_and_applies_the_global_limit()
    {
        var older = new IntegrationEventOutboxDeadLetterEntry(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "event.old",
            "blog",
            DateTimeOffset.Parse("2026-04-10T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            3,
            "old",
            "old message");
        var newer = new IntegrationEventOutboxDeadLetterEntry(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "event.new",
            "admin",
            DateTimeOffset.Parse("2026-04-12T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            5,
            "new",
            "new message");

        var firstStore = new RecordingPlatformOutboxStore("blog") { DeadLetters = [older] };
        var secondStore = new RecordingPlatformOutboxStore("admin") { DeadLetters = [newer] };
        var handler = new GetOutboxDeadLettersQueryHandler([firstStore, secondStore]);

        var result = await handler.Handle(new GetOutboxDeadLettersQuery(Limit: 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, firstStore.LastDeadLetterLimit);
        Assert.Equal(1, secondStore.LastDeadLetterLimit);
        var entry = Assert.Single(result.Value!.Entries);
        Assert.Equal(newer.EventId, entry.EventId);
    }
}

public sealed class GetPlatformAuditEventsQueryHandlerTests
{
    [Fact]
    public async Task Handle_rejects_a_malformed_cursor()
    {
        var handler = new GetPlatformAuditEventsQueryHandler(new RecordingPlatformAuditEventReader());

        var result = await handler.Handle(new GetPlatformAuditEventsQuery(Limit: 10, After: "not-a-cursor"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PlatformAuditErrors.InvalidCursor().Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_applies_the_query_limit_and_returns_the_paged_result()
    {
        var auditEntry = PlatformTestData.CreateAuditEntry("platform", "action", "module", "platform", "enabled");
        var reader = new RecordingPlatformAuditEventReader
        {
            PagedResult = new CursorPagedResult<PlatformAuditEntry>([auditEntry], null)
        };
        var handler = new GetPlatformAuditEventsQueryHandler(reader);

        var result = await handler.Handle(new GetPlatformAuditEventsQuery(Limit: 999), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, reader.LastLimit);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(auditEntry, item);
    }
}
