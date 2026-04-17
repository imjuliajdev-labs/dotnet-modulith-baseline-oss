using BuildingBlocks.Application.Modules;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Tests;

public sealed class ModuleBulkheadIntegrationTests
{
    private const string AlphaWorkPath = "/api/v1/alpha/work";
    private const string BetaWorkPath = "/api/v1/beta/work";

    [Fact]
    public async Task SaturatingOneModuleDoesNotRejectUnrelatedModuleRequests()
    {
        using var gate = new ModuleBulkheadGate();
        await using var app = await ModuleBulkheadTestApplication.StartAsync(gate);

        using var client = app.CreateClient();

        var firstAlphaRequest = client.GetAsync(AlphaWorkPath);
        await gate.WaitUntilAlphaEnteredAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var rejectedAlphaResponseTask = client.GetAsync(AlphaWorkPath);
        var betaResponseTask = client.GetAsync(BetaWorkPath);

        var rejectedAlphaResponse = await rejectedAlphaResponseTask;
        var betaResponse = await betaResponseTask;

        Assert.Equal(StatusCodes.Status429TooManyRequests, (int)rejectedAlphaResponse.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, (int)betaResponse.StatusCode);

        gate.ReleaseAlpha();

        var firstAlphaResponse = await firstAlphaRequest;
        Assert.Equal(StatusCodes.Status200OK, (int)firstAlphaResponse.StatusCode);
    }

    private sealed class ModuleBulkheadTestApplication : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private ModuleBulkheadTestApplication(WebApplication application)
        {
            _application = application;
        }

        public static async Task<ModuleBulkheadTestApplication> StartAsync(ModuleBulkheadGate gate)
        {
            ArgumentNullException.ThrowIfNull(gate);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BaselineDatabase"] = "Host=localhost;Database=baseline_bulkhead_unused;Username=postgres;Password=postgres"
            });

            builder.Services.AddSingleton(gate);
            builder.Services.AddBuildingBlocksInfrastructureDefaults();
            builder.Services.AddSingleton<IModuleWorkLeaseManager, NoOpModuleWorkLeaseManager>();
            builder.Services.AddSingleton<IModuleExecutionGate, ModuleExecutionGate>();
            builder.Services.AddApiModule<AlphaModule>();
            builder.Services.AddApiModule<BetaModule>();
            builder.Services.AddRateLimiter(static _ =>
            {
            });
            builder.Services.AddModuleEndpointBulkheads(permitLimit: 1, queueLimit: 0);
            RemoveHostedService(builder.Services, "SharedRuntimePersistenceConfigurationValidationHostedService");
            RemoveHostedService(builder.Services, "DatabaseMigrationReadinessHostedService");
            RemoveHostedService(builder.Services, "IntegrationEventOutboxHostedService");

            var application = builder.Build();
            application.UseExceptionHandler();
            application.UseRateLimiter();
            application.MapApiModules();

            await application.StartAsync();
            return new ModuleBulkheadTestApplication(application);
        }

        public HttpClient CreateClient()
        {
            return _application.GetTestClient();
        }

        public async ValueTask DisposeAsync()
        {
            await _application.DisposeAsync();
        }

        private static void RemoveHostedService(IServiceCollection services, string implementationTypeName)
        {
            for (var index = services.Count - 1; index >= 0; index--)
            {
                var descriptor = services[index];
                if (descriptor.ServiceType != typeof(IHostedService))
                {
                    continue;
                }

                if (!string.Equals(descriptor.ImplementationType?.Name, implementationTypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                services.RemoveAt(index);
            }
        }
    }

    private sealed class ModuleBulkheadGate : IDisposable
    {
        private readonly TaskCompletionSource _alphaEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseAlpha = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilAlphaEnteredAsync()
        {
            return _alphaEntered.Task;
        }

        public async Task WaitForAlphaReleaseAsync(CancellationToken cancellationToken)
        {
            _alphaEntered.TrySetResult();
            await _releaseAlpha.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseAlpha()
        {
            _releaseAlpha.TrySetResult();
        }

        public void Dispose()
        {
            _releaseAlpha.TrySetCanceled();
        }
    }

    private sealed class AlphaModule : IApiModule
    {
        public ModuleDescriptor Descriptor { get; } = new(
            Key: "alpha",
            DisplayName: "Alpha",
            RoutePrefix: "/alpha",
            SchemaName: "alpha",
            ModuleNamespace: "alpha",
            DefaultEnabled: true,
            CanBeDisabled: true);

        public string Key => Descriptor.Key;

        public void AddServices(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapGet(
                "/work",
                static async (ModuleBulkheadGate gate, CancellationToken cancellationToken) =>
                {
                    await gate.WaitForAlphaReleaseAsync(cancellationToken);
                    return Results.Ok(new { status = "alpha-complete" });
                });
        }
    }

    private sealed class BetaModule : IApiModule
    {
        public ModuleDescriptor Descriptor { get; } = new(
            Key: "beta",
            DisplayName: "Beta",
            RoutePrefix: "/beta",
            SchemaName: "beta",
            ModuleNamespace: "beta",
            DefaultEnabled: true,
            CanBeDisabled: true);

        public string Key => Descriptor.Key;

        public void AddServices(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapGet(
                "/work",
                static () => Results.Ok(new { status = "beta-complete" }));
        }
    }
}
