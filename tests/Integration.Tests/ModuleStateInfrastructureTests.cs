using System.Text.Json;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Modules;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Modules;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Integration.Tests;

public sealed class ModuleStateInfrastructureTests
{
    [Xunit.Fact]
    public async Task DescriptorBackedModuleStateReaderUsesModuleDescriptorDefaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModule>(new TestModule("reports", defaultEnabled: false, canBeDisabled: true));
        services.AddBuildingBlocksInfrastructureDefaults();

        await using var provider = services.BuildServiceProvider();
        var reader = provider.GetRequiredService<IModuleStateReader>();

        var state = await reader.GetRequiredStateAsync("reports", CancellationToken.None);

        Xunit.Assert.Equal(ModuleRuntimeState.Disabled, state.RuntimeState);
    }

    [Xunit.Fact]
    public async Task ModuleStateEndpointFilterUsesTheSharedProblemDetailsWriterWhenTheModuleIsNotEnabled()
    {
        var httpContext = CreateHttpContext("/api/v1/admin/users");
        httpContext.RequestServices = BuildServices(
            new Error("module.disabled", "Module 'admin' is disabled.", ErrorKind.ServiceUnavailable));

        var filter = new ModuleStateEndpointFilter("admin");
        var result = await filter.InvokeAsync(
            new TestEndpointFilterInvocationContext(httpContext),
            _ => ValueTask.FromResult<object?>("should-not-run"));

        var httpResult = Xunit.Assert.IsAssignableFrom<IResult>(result);
        await httpResult.ExecuteAsync(httpContext);

        Xunit.Assert.Equal(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
        Xunit.Assert.Equal("application/problem+json", httpContext.Response.ContentType);

        using var json = await ReadJsonAsync(httpContext);
        Xunit.Assert.Equal("module.disabled", json.RootElement.GetProperty("code").GetString());
        Xunit.Assert.Equal("/api/v1/admin/users", json.RootElement.GetProperty("instance").GetString());
    }

    [Xunit.Fact]
    public async Task ModuleStateEndpointFilterAllowsEnabledModulesToProceed()
    {
        var httpContext = CreateHttpContext("/api/v1/admin/users");
        httpContext.RequestServices = BuildServices(Error.None);

        var filter = new ModuleStateEndpointFilter("admin");
        var nextCalled = false;

        var result = await filter.InvokeAsync(
            new TestEndpointFilterInvocationContext(httpContext),
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>("next-ran");
            });

        Xunit.Assert.True(nextCalled);
        Xunit.Assert.Equal("next-ran", result);
    }

    [Xunit.Fact]
    public async Task ModulePollingBackgroundServiceSkipsIterationsWhenTheExecutionGateDeniesEntry()
    {
        var leaseManager = new TrackingModuleWorkLeaseManager();
        var executionGate = new ModuleExecutionGate(
            new StubModuleStateGuard(new Error("module.disabled", "Module 'reports' is disabled.", ErrorKind.ServiceUnavailable)),
            leaseManager);
        var worker = new TestWorker(executionGate);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(120));
        await worker.StopAsync(CancellationToken.None);

        Xunit.Assert.Equal(0, worker.IterationCount);
        Xunit.Assert.Equal(0, leaseManager.AcquireCount);
        Xunit.Assert.Equal(0, leaseManager.ReleaseCount);
    }

    [Xunit.Fact]
    public async Task ModulePollingBackgroundServiceUsesTheSharedExecutionGateLeaseForEnabledIterations()
    {
        var leaseManager = new TrackingModuleWorkLeaseManager();
        var executionGate = new ModuleExecutionGate(
            new SequenceModuleStateGuard(
            [
                Error.None,
                Error.None,
                new Error("module.disabled", "Module 'reports' is disabled.", ErrorKind.ServiceUnavailable)
            ]),
            leaseManager);
        var worker = new TestWorker(executionGate);

        await worker.StartAsync(CancellationToken.None);
        await worker.WaitForIterationAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Xunit.Assert.Equal(1, worker.IterationCount);
        Xunit.Assert.Equal(1, leaseManager.AcquireCount);
        Xunit.Assert.Equal(1, leaseManager.ReleaseCount);
    }

    private static ServiceProvider BuildServices(Error failureOrNone)
    {
        var services = new ServiceCollection();
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(failureOrNone));
        services.AddSingleton<IModuleWorkLeaseManager, NoOpModuleWorkLeaseManager>();
        services.AddSingleton<IModuleExecutionGate, ModuleExecutionGate>();
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateHttpContext(PathString path)
    {
        return new DefaultHttpContext
        {
            Request =
            {
                Path = path
            },
            Response =
            {
                Body = new MemoryStream()
            }
        };
    }

    private static async Task<JsonDocument> ReadJsonAsync(DefaultHttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(httpContext.Response.Body);
    }

    private sealed class TestEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        private readonly object?[] _arguments = Array.Empty<object?>();

        public TestEndpointFilterInvocationContext(HttpContext httpContext)
        {
            HttpContext = httpContext;
        }

        public override HttpContext HttpContext { get; }

        public override IList<object?> Arguments => _arguments;

        public override T GetArgument<T>(int index)
        {
            return (T)_arguments[index]!;
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

    private sealed class SequenceModuleStateGuard : IModuleStateGuard
    {
        private readonly Queue<Error> _results;

        public SequenceModuleStateGuard(IEnumerable<Error> results)
        {
            _results = new Queue<Error>(results);
        }

        public ValueTask<Error> GetFailureOrNoneAsync(string moduleKey, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
            return ValueTask.FromResult(_results.Count == 0 ? Error.None : _results.Dequeue());
        }
    }

    private sealed class TrackingModuleWorkLeaseManager : IModuleWorkLeaseManager
    {
        public int AcquireCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public ValueTask<IAsyncDisposable> AcquireAsync(string moduleKey, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
            AcquireCount++;
            return ValueTask.FromResult<IAsyncDisposable>(new TrackingLease(this));
        }

        private sealed class TrackingLease : IAsyncDisposable
        {
            private readonly TrackingModuleWorkLeaseManager _owner;
            private int _disposed;

            public TrackingLease(TrackingModuleWorkLeaseManager owner)
            {
                _owner = owner;
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.ReleaseCount++;
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TestModule : IModule
    {
        public TestModule(string key, bool defaultEnabled, bool canBeDisabled)
        {
            Descriptor = new ModuleDescriptor(
                key,
                key,
                "/" + key,
                key.Replace('-', '_'),
                key,
                defaultEnabled,
                canBeDisabled);
        }

        public ModuleDescriptor Descriptor { get; }

        public string Key => Descriptor.Key;
    }

    private sealed class TestWorker : ModulePollingBackgroundService
    {
        private readonly TaskCompletionSource _iterationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TestWorker(IModuleExecutionGate moduleExecutionGate)
            : base(moduleExecutionGate, TimeSpan.FromMilliseconds(20), NullLogger<TestWorker>.Instance)
        {
        }

        public int IterationCount { get; private set; }

        public Task WaitForIterationAsync()
        {
            return _iterationCompleted.Task;
        }

        protected override string ModuleKey => "reports";

        protected override Task ExecuteEnabledIterationAsync(CancellationToken stoppingToken)
        {
            IterationCount++;
            _iterationCompleted.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
