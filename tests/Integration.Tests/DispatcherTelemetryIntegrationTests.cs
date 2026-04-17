using System.Diagnostics;
using System.Collections.Concurrent;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

public sealed class DispatcherTelemetryIntegrationTests
{
    [Xunit.Fact]
    public async Task BuildingBlocksInfrastructureDefaultsEmitDispatcherActivitiesWithRequestContextTags()
    {
        var stoppedActivities = new ConcurrentQueue<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == ActivityRequestTelemetrySessionFactory.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stoppedActivities.Enqueue(activity)
        };

        ActivitySource.AddActivityListener(listener);

        var services = new ServiceCollection();
        services.AddDispatcher();
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddScoped<ICommandHandler<TelemetryIntegrationCommand, string>, TelemetryIntegrationCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var requestContextAccessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();

        requestContextAccessor.Current = new RequestContext("corr-456", "req-123");

        var result = await dispatcher.Send(new TelemetryIntegrationCommand("admin", "ok"));

        Xunit.Assert.True(result.IsSuccess);

        var activity = Xunit.Assert.Single(stoppedActivities.Where(static item => item.DisplayName == "TelemetryIntegrationCommand"));
        Xunit.Assert.Equal("TelemetryIntegrationCommand", activity.DisplayName);
        Xunit.Assert.Equal(ActivityStatusCode.Ok, activity.Status);

        var tags = activity.Tags.ToDictionary(static tag => tag.Key, static tag => tag.Value);
        Xunit.Assert.Equal(typeof(TelemetryIntegrationCommand).FullName, tags["dispatcher.request.type"]);
        Xunit.Assert.Equal("command", tags["dispatcher.request.kind"]);
        Xunit.Assert.Equal("admin", tags["dispatcher.module.key"]);
        Xunit.Assert.Equal("corr-456", tags["correlation.id"]);
        Xunit.Assert.Equal("req-123", tags["request.id"]);
        Xunit.Assert.Equal("success", tags["dispatcher.outcome"]);
    }

    public sealed record TelemetryIntegrationCommand(string ModuleKey, string Value) : ICommand<string>, IModuleScoped;

    private sealed class TelemetryIntegrationCommandHandler : ICommandHandler<TelemetryIntegrationCommand, string>
    {
        public Task<Result<string>> Handle(TelemetryIntegrationCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    private sealed class StubModuleStateGuard : IModuleStateGuard
    {
        private readonly Error _error;

        public StubModuleStateGuard(Error error)
        {
            _error = error;
        }

        public ValueTask<Error> GetFailureOrNoneAsync(string moduleKey, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_error);
        }
    }
}
