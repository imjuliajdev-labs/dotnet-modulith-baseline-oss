using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Dispatching;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.Modules;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Identity.Api.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Application.ModuleState;
using Platform.Infrastructure;
using Platform.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests;

public sealed class PlatformModuleStatePersistenceIntegrationTests
{
    [Xunit.Fact]
    public async Task ApplicationStartupFailsFastWhenSharedRuntimeDatabaseConfigurationIsMissing()
    {
        var exception = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresBackedApiApplication.StartAsync(
                string.Empty,
                applyMigrations: false,
                configureBuilder: builder => builder.Services.AddApiModule<PlatformModuleStateIntegrationTests.ReportsModule>()));

        Xunit.Assert.Contains(SharedRuntimePersistenceDefaults.MissingConnectionStringMessage, exception.Message, StringComparison.Ordinal);
    }

    [Xunit.Fact]
    public async Task ApplicationStartupFailsFastWhenConfiguredPostgresMigrationsHaveNotBeenApplied()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync();

        var exception = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresBackedApiApplication.StartAsync(
                postgres.GetConnectionString(),
                applyMigrations: false,
                configureBuilder: builder => builder.Services.AddApiModule<PlatformModuleStateIntegrationTests.ReportsModule>()));

        Xunit.Assert.Contains("Run DbMigrator before starting the host.", exception.Message, StringComparison.Ordinal);
    }

    [Xunit.Fact]
    public async Task PlatformModuleStateAndAuditPersistAcrossApplicationRestartWhenPostgresIsConfigured()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());

        await PostgresBackedApiApplication.RunDbMigratorAsync(configuration);

        await using (var application = await PostgresBackedApiApplication.StartAsync(
            postgres.GetConnectionString(),
            applyMigrations: false,
            configureBuilder: builder => builder.Services.AddApiModule<PlatformModuleStateIntegrationTests.ReportsModule>()))
        {
            var client = application.App.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");

            var unavailable = await SendAsync(client, HttpMethod.Get, "/api/v1/reports/status");
            Xunit.Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);

            var session = await SignInAsync(client);


            var authCookie = session.Cookie;


            var cookies = session.Cookies;

            var enable = await SendAsync(
                client,
                HttpMethod.Post,
                "/api/v1/platform/modules/reports/enable",
                cookies,
                session.HeaderName,
                session.RequestToken);

            Xunit.Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        }

        await using (var application = await PostgresBackedApiApplication.StartAsync(
            postgres.GetConnectionString(),
            applyMigrations: false,
            configureBuilder: builder => builder.Services.AddApiModule<PlatformModuleStateIntegrationTests.ReportsModule>()))
        {
            var client = application.App.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");

            var available = await SendAsync(client, HttpMethod.Get, "/api/v1/reports/status");
            Xunit.Assert.Equal(HttpStatusCode.OK, available.StatusCode);

            var signInResult = await SignInAsync(client);


            var authCookie = signInResult.Cookie;

            var audit = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/audit-events?limit=1", authCookie);
            Xunit.Assert.Equal(HttpStatusCode.OK, audit.StatusCode);

            using var json = await ReadJsonAsync(audit);
            var entry = json.RootElement.GetProperty("events").EnumerateArray().Single();
            Xunit.Assert.Equal("platform.module_state.enable", entry.GetProperty("action").GetString());
            Xunit.Assert.Equal("reports", entry.GetProperty("targetId").GetString());
            Xunit.Assert.Equal("identity:seeded-admin", entry.GetProperty("actorId").GetString());
        }
    }

    [Xunit.Fact]
    public async Task TransitionStatesConvergeAcrossApplicationsSharingTheSamePostgresStore()
    {
        await using var harness = await MultiInstanceHarness.CreateAsync();

        var participant = new ReportsTransitionParticipant();
        var primary = await harness.CreateInstanceAsync(
            services => services.AddSingleton<IPlatformModuleTransitionParticipant>(participant));
        var secondary = await harness.CreateInstanceAsync();

        var primaryClient = harness.GetClient(primary);
        var secondaryClient = harness.GetClient(secondary);

        await harness.AuthenticateAsync(primaryClient);

        var enableTask = harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable");

        await participant.WaitForEnableEnteredAsync().WaitAsync(TimeSpan.FromSeconds(10));

        using (var enablingJson = await ReadJsonAsync(await SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/platform/modules")))
        {
            var reports = GetModule(enablingJson, "reports");
            Xunit.Assert.Equal("enabled", reports.GetProperty("desiredState").GetString());
            Xunit.Assert.Equal("enabling", reports.GetProperty("runtimeState").GetString());
        }

        var enablingUnavailable = await SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(enablingUnavailable, HttpStatusCode.ServiceUnavailable, "module.enabling");

        participant.ReleaseEnable();

        var enableResponse = await enableTask.WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var enabledResponse = await SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/reports/status");
        Xunit.Assert.Equal(HttpStatusCode.OK, enabledResponse.StatusCode);

        var disableTask = harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable");

        await participant.WaitForDisableEnteredAsync().WaitAsync(TimeSpan.FromSeconds(10));

        using (var disablingJson = await ReadJsonAsync(await SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/platform/modules")))
        {
            var reports = GetModule(disablingJson, "reports");
            Xunit.Assert.Equal("disabled", reports.GetProperty("desiredState").GetString());
            Xunit.Assert.Equal("disabling", reports.GetProperty("runtimeState").GetString());
        }

        var disablingUnavailable = await SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(disablingUnavailable, HttpStatusCode.ServiceUnavailable, "module.disabling");

        participant.ReleaseDisable();

        var disableResponse = await disableTask.WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.True(
            disableResponse.StatusCode == HttpStatusCode.OK || (int)disableResponse.StatusCode == 499,
            $"Expected 200 or 499 but got {(int)disableResponse.StatusCode} ({disableResponse.StatusCode}).");

        var disabledResponse = await SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(disabledResponse, HttpStatusCode.ServiceUnavailable, "module.disabled");
    }

    [Xunit.Fact]
    public async Task DisableWaitsForInFlightWorkBeforeFinalizingAcrossApplicationsSharingTheSamePostgresStore()
    {
        await using var harness = await MultiInstanceHarness.CreateAsync();

        var transitionParticipant = new ReportsDisableSignalParticipant();
        var coordinator = new ReportsDrainCoordinator();

        var primary = await harness.CreateInstanceAsync(
            services =>
            {
                services.AddSingleton<IPlatformModuleTransitionParticipant>(transitionParticipant);
                services.AddSingleton<IReportsWorkCoordinator>(coordinator);
            });
        var secondary = await harness.CreateInstanceAsync(
            services => services.AddSingleton<IReportsWorkCoordinator>(coordinator));

        var primaryClient = harness.GetClient(primary);
        var secondaryClient = harness.GetClient(secondary);

        await harness.AuthenticateAsync(primaryClient);

        var enableResponse = await harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable");
        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var workTask = SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/reports/drain");
        await coordinator.WaitForWorkStartedAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var disableTask = harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable");

        await transitionParticipant.WaitForDisableEnteredAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.False(disableTask.IsCompleted);

        var disablingUnavailable = await SendAsync(primaryClient, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(disablingUnavailable, HttpStatusCode.ServiceUnavailable, "module.disabling");

        coordinator.ReleaseWork();

        var workResponse = await workTask.WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.Equal(HttpStatusCode.OK, workResponse.StatusCode);

        var disableResponse = await disableTask.WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        var disabledResponse = await SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(disabledResponse, HttpStatusCode.ServiceUnavailable, "module.disabled");
    }

    [Xunit.Fact]
    public async Task DisableWaitsForInFlightIntegrationEventHandlerAcrossApplicationsSharingTheSamePostgresStore()
    {
        await using var harness = await MultiInstanceHarness.CreateAsync();

        var transitionParticipant = new ReportsDisableSignalParticipant();
        var coordinator = new ReportsIntegrationEventCoordinator();

        var primary = await harness.CreateInstanceAsync(
            services => services.AddSingleton<IPlatformModuleTransitionParticipant>(transitionParticipant));
        var secondary = await harness.CreateInstanceAsync(
            services =>
            {
                services.AddSingleton(coordinator);
                services.AddSingleton<IIntegrationEventHandler<ReportsIntegrationEvent>, ReportsIntegrationEventHandler>();
            });

        var primaryClient = harness.GetClient(primary);

        await harness.AuthenticateAsync(primaryClient);

        var enableResponse = await harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable");
        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        using var scope = secondary.App.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
        var publishTask = dispatcher.PublishAsync(new ReportsIntegrationEvent(Guid.NewGuid(), NodaTime.SystemClock.Instance.GetCurrentInstant()));

        await coordinator.WaitForHandlerStartedAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var disableTask = harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable");

        await transitionParticipant.WaitForDisableEnteredAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.False(disableTask.IsCompleted);

        coordinator.ReleaseHandler();

        await publishTask.WaitAsync(TimeSpan.FromSeconds(10));

        var disableResponse = await disableTask.WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
    }

    [Xunit.Fact]
    public async Task ThreeInstancesConvergeOnModuleStateWhenPrimaryDrivesTransition()
    {
        await using var harness = await MultiInstanceHarness.CreateAsync();

        var primary = await harness.CreateInstanceAsync();
        var secondary = await harness.CreateInstanceAsync();
        var tertiary = await harness.CreateInstanceAsync();

        var primaryClient = harness.GetClient(primary);
        var secondaryClient = harness.GetClient(secondary);
        var tertiaryClient = harness.GetClient(tertiary);

        await harness.AuthenticateAsync(primaryClient);

        var enableResponse = await harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable");

        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var primaryEnabled = await SendAsync(primaryClient, HttpMethod.Get, "/api/v1/reports/status");
        Xunit.Assert.Equal(HttpStatusCode.OK, primaryEnabled.StatusCode);

        var secondaryEnabled = await SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/reports/status");
        Xunit.Assert.Equal(HttpStatusCode.OK, secondaryEnabled.StatusCode);

        var tertiaryEnabled = await SendAsync(tertiaryClient, HttpMethod.Get, "/api/v1/reports/status");
        Xunit.Assert.Equal(HttpStatusCode.OK, tertiaryEnabled.StatusCode);

        var disableResponse = await harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable");

        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        var primaryDisabled = await SendAsync(primaryClient, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(primaryDisabled, HttpStatusCode.ServiceUnavailable, "module.disabled");

        var secondaryDisabled = await SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(secondaryDisabled, HttpStatusCode.ServiceUnavailable, "module.disabled");

        var tertiaryDisabled = await SendAsync(tertiaryClient, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(tertiaryDisabled, HttpStatusCode.ServiceUnavailable, "module.disabled");
    }

    [Xunit.Fact]
    public async Task EnableTransitionCompletesWhenSecondaryInstanceIsDisposedMidTransition()
    {
        await using var harness = await MultiInstanceHarness.CreateAsync();

        var participant = new ReportsTransitionParticipant();
        var primary = await harness.CreateInstanceAsync(
            services => services.AddSingleton<IPlatformModuleTransitionParticipant>(participant));
        var secondary = await harness.CreateInstanceAsync();

        var primaryClient = harness.GetClient(primary);

        await harness.AuthenticateAsync(primaryClient);

        var enableTask = harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable");

        await participant.WaitForEnableEnteredAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var secondaryDisposeTask = harness.DisposeInstanceAbruptlyAsync(secondary);

        participant.ReleaseEnable();

        var enableResponse = await enableTask.WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        await secondaryDisposeTask.WaitAsync(TimeSpan.FromSeconds(10));

        var freshSecondary = await harness.CreateInstanceAsync();
        var freshSecondaryClient = harness.GetClient(freshSecondary);

        var enabledResponse = await SendAsync(freshSecondaryClient, HttpMethod.Get, "/api/v1/reports/status");
        Xunit.Assert.Equal(HttpStatusCode.OK, enabledResponse.StatusCode);
    }

    [Xunit.Fact]
    public async Task DisableTransitionCompletesWhenSecondaryInstanceIsDisposedMidTransition()
    {
        await using var harness = await MultiInstanceHarness.CreateAsync();

        var participant = new ReportsTransitionParticipant();
        var primary = await harness.CreateInstanceAsync(
            services => services.AddSingleton<IPlatformModuleTransitionParticipant>(participant));
        var secondary = await harness.CreateInstanceAsync();

        var primaryClient = harness.GetClient(primary);

        await harness.AuthenticateAsync(primaryClient);

        var enableTask = harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable");

        await participant.WaitForEnableEnteredAsync().WaitAsync(TimeSpan.FromSeconds(10));
        participant.ReleaseEnable();

        var enableResponse = await enableTask.WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var disableTask = harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable");

        await participant.WaitForDisableEnteredAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var secondaryDisposeTask = harness.DisposeInstanceAbruptlyAsync(secondary);

        participant.ReleaseDisable();

        var disableResponse = await disableTask.WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        await secondaryDisposeTask.WaitAsync(TimeSpan.FromSeconds(10));

        var freshSecondary = await harness.CreateInstanceAsync();
        var freshSecondaryClient = harness.GetClient(freshSecondary);

        await AssertModuleDisabledAsync(freshSecondaryClient, TimeSpan.FromSeconds(10));
    }

    [Xunit.Fact]
    public async Task RestartDuringDrainCompletesGracefully()
    {
        await using var harness = await MultiInstanceHarness.CreateAsync();

        var primary = await harness.CreateInstanceAsync();
        var secondary = await harness.CreateInstanceAsync();

        var primaryClient = harness.GetClient(primary);

        await harness.AuthenticateAsync(primaryClient);

        var enableResponse = await harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable");

        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        await secondary.DisposeAsync();

        var disableResponse = await harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable");

        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        var freshSecondary = await harness.CreateInstanceAsync();
        var freshSecondaryClient = harness.GetClient(freshSecondary);

        var disabledResponse = await SendAsync(freshSecondaryClient, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(disabledResponse, HttpStatusCode.ServiceUnavailable, "module.disabled");
    }

    [Xunit.Fact]
    public async Task CrossInstanceConvergenceCompletesWithinBoundedTime()
    {
        await using var harness = await MultiInstanceHarness.CreateAsync();

        var primary = await harness.CreateInstanceAsync();
        var secondary = await harness.CreateInstanceAsync();
        var tertiary = await harness.CreateInstanceAsync();

        var primaryClient = harness.GetClient(primary);
        var secondaryClient = harness.GetClient(secondary);
        var tertiaryClient = harness.GetClient(tertiary);

        await harness.AuthenticateAsync(primaryClient);

        var stopwatch = Stopwatch.StartNew();

        var enableResponse = await harness.SendAuthenticatedAsync(
            primaryClient,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable");

        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var secondaryEnabled = await SendAsync(secondaryClient, HttpMethod.Get, "/api/v1/reports/status");
        Xunit.Assert.Equal(HttpStatusCode.OK, secondaryEnabled.StatusCode);

        var tertiaryEnabled = await SendAsync(tertiaryClient, HttpMethod.Get, "/api/v1/reports/status");
        Xunit.Assert.Equal(HttpStatusCode.OK, tertiaryEnabled.StatusCode);

        stopwatch.Stop();

        Xunit.Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Cross-instance convergence took {stopwatch.Elapsed.TotalMilliseconds:F0}ms, which exceeds the 10-second bound.");
    }

    private static Task<PostgresBackedApiApplication> CreateReportsApplicationAsync(string connectionString, Action<IServiceCollection>? configureServices = null)
    {
        return PostgresBackedApiApplication.StartAsync(
            connectionString,
            applyMigrations: false,
            configureBuilder: builder =>
            {
                builder.Services.AddApiModule<PersistenceReportsModule>();
                builder.Services.AddPostgresIntegrationEventInbox("reports", "reports");
                configureServices?.Invoke(builder.Services);
            });
    }
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode, string expectedCode)
    {
        Xunit.Assert.Equal(expectedStatusCode, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertModuleDisabledAsync(HttpClient client, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);

        while (!deadline.Token.IsCancellationRequested)
        {
            var response = await SendAsync(client, HttpMethod.Get, "/api/v1/reports/status");
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                using var json = await ReadJsonAsync(response);
                if (string.Equals(json.RootElement.GetProperty("code").GetString(), "module.disabled", StringComparison.Ordinal))
                {
                    return;
                }
            }

            try
            {
                await Task.Delay(100, deadline.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Xunit.Assert.Fail("Reports module did not converge to the disabled state within the expected time window.");
    }

    private static JsonElement GetModule(JsonDocument document, string moduleKey)
    {
        return document.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .Single(element => string.Equals(element.GetProperty("key").GetString(), moduleKey, StringComparison.Ordinal));
    }

    private interface IReportsWorkCoordinator
    {
        Task ExecuteAsync(CancellationToken cancellationToken);
    }

    public sealed class PersistenceReportsModule : IApiModule
    {
        public ModuleDescriptor Descriptor { get; } = new(
            Key: "reports",
            DisplayName: "Reports",
            RoutePrefix: "/reports",
            SchemaName: "reports",
            ModuleNamespace: "reports",
            DefaultEnabled: false,
            CanBeDisabled: true);

        public string Key => Descriptor.Key;

        public void AddServices(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddScoped<IQueryHandler<GetReportsStatusQuery, string>, GetReportsStatusQueryHandler>();
            services.AddScoped<IQueryHandler<DrainReportsQuery, string>, DrainReportsQueryHandler>();
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapGet(
                    "/status",
                    static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                    {
                        var result = await dispatcher.Query(new GetReportsStatusQuery(), cancellationToken);
                        return mapper.Match(result, httpContext, status => Results.Ok(new ReportsStatusResponse(status!)));
                    })
                .WithName("PersistenceReports_GetStatus");

            endpoints.MapGet(
                    "/drain",
                    static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                    {
                        var result = await dispatcher.Query(new DrainReportsQuery(), cancellationToken);
                        return mapper.Match(result, httpContext, status => Results.Ok(new ReportsStatusResponse(status!)));
                    })
                .WithName("PersistenceReports_Drain");
        }
    }

    private sealed record GetReportsStatusQuery : IQuery<string>, IModuleScoped
    {
        public string ModuleKey => "reports";
    }

    private sealed record DrainReportsQuery : IQuery<string>, IModuleScoped
    {
        public string ModuleKey => "reports";
    }

    private sealed class GetReportsStatusQueryHandler : IQueryHandler<GetReportsStatusQuery, string>
    {
        public Task<Result<string>> Handle(GetReportsStatusQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<string>.Success("online"));
        }
    }

    private sealed class DrainReportsQueryHandler : IQueryHandler<DrainReportsQuery, string>
    {
        private readonly IReportsWorkCoordinator? _coordinator;

        public DrainReportsQueryHandler(IEnumerable<IReportsWorkCoordinator> coordinators)
        {
            _coordinator = coordinators.LastOrDefault();
        }

        public async Task<Result<string>> Handle(DrainReportsQuery query, CancellationToken cancellationToken)
        {
            if (_coordinator is not null)
            {
                await _coordinator.ExecuteAsync(cancellationToken);
            }

            return Result<string>.Success("drained");
        }
    }

    private sealed record ReportsStatusResponse(string Status);

    private sealed class ReportsTransitionParticipant : IPlatformModuleTransitionParticipant
    {
        private readonly TaskCompletionSource _disableEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _enableEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseDisable = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseEnable = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ModuleKey => "reports";

        public Task WaitForEnableEnteredAsync()
        {
            return _enableEntered.Task;
        }

        public Task WaitForDisableEnteredAsync()
        {
            return _disableEntered.Task;
        }

        public void ReleaseEnable()
        {
            _releaseEnable.TrySetResult();
        }

        public void ReleaseDisable()
        {
            _releaseDisable.TrySetResult();
        }

        public async Task OnEnablingAsync(CancellationToken cancellationToken)
        {
            _enableEntered.TrySetResult();
            await _releaseEnable.Task.WaitAsync(cancellationToken);
        }

        public async Task OnDisablingAsync(CancellationToken cancellationToken)
        {
            _disableEntered.TrySetResult();
            await _releaseDisable.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ReportsDisableSignalParticipant : IPlatformModuleTransitionParticipant
    {
        private readonly TaskCompletionSource _disableEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ModuleKey => "reports";

        public Task WaitForDisableEnteredAsync()
        {
            return _disableEntered.Task;
        }

        public Task OnEnablingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task OnDisablingAsync(CancellationToken cancellationToken)
        {
            _disableEntered.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class ReportsDrainCoordinator : IReportsWorkCoordinator
    {
        private readonly TaskCompletionSource _releaseWork = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _workStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForWorkStartedAsync()
        {
            return _workStarted.Task;
        }

        public void ReleaseWork()
        {
            _releaseWork.TrySetResult();
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _workStarted.TrySetResult();
            await _releaseWork.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ReportsIntegrationEventCoordinator
    {
        private readonly TaskCompletionSource _handlerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseHandler = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForHandlerStartedAsync()
        {
            return _handlerStarted.Task;
        }

        public void ReleaseHandler()
        {
            _releaseHandler.TrySetResult();
        }

        public async Task HandleAsync(CancellationToken cancellationToken)
        {
            _handlerStarted.TrySetResult();
            await _releaseHandler.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed record ReportsIntegrationEvent(Guid EventId, NodaTime.Instant OccurredAt) : IIntegrationEvent;

    private sealed class ReportsIntegrationEventHandler : IModuleScopedIntegrationEventHandler<ReportsIntegrationEvent>
    {
        private readonly ReportsIntegrationEventCoordinator _coordinator;

        public ReportsIntegrationEventHandler(ReportsIntegrationEventCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        public string ModuleKey => "reports";

        public Task Handle(ReportsIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(integrationEvent);
            return _coordinator.HandleAsync(cancellationToken);
        }
    }

    private sealed class MultiInstanceHarness : IAsyncDisposable
    {
        private readonly PostgreSqlContainer _postgres;
        private readonly List<PostgresBackedApiApplication> _instances = new();
        private string? _authCookie;
        private string? _cookies;
        private string? _antiforgeryHeaderName;
        private string? _antiforgeryToken;

        private MultiInstanceHarness(PostgreSqlContainer postgres)
        {
            _postgres = postgres;
        }

        public static async Task<MultiInstanceHarness> CreateAsync()
        {
            var postgres = await PostgresBackedApiApplication.StartPostgresAsync();
            var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());
            await PostgresBackedApiApplication.RunDbMigratorAsync(
                configuration,
                configureExtraMigrations: static services => services.AddPostgresIntegrationEventInbox("reports", "reports"));
            return new MultiInstanceHarness(postgres);
        }

        public async Task<PostgresBackedApiApplication> CreateInstanceAsync(Action<IServiceCollection>? configureServices = null)
        {
            var instance = await CreateReportsApplicationAsync(_postgres.GetConnectionString(), configureServices);
            _instances.Add(instance);
            return instance;
        }

        public async Task DisposeInstanceAbruptlyAsync(PostgresBackedApiApplication instance)
        {
            ArgumentNullException.ThrowIfNull(instance);

            if (!_instances.Remove(instance))
            {
                return;
            }

            await instance.App.DisposeAsync();
        }

        public HttpClient GetClient(PostgresBackedApiApplication instance)
        {
            var client = instance.App.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");
            return client;
        }

        public async Task AuthenticateAsync(HttpClient primaryClient)
        {
            var authSession = await SignInAsync(primaryClient);
            _authCookie = authSession.Cookie;
            _cookies = authSession.Cookies;
            _antiforgeryHeaderName = authSession.HeaderName;
            _antiforgeryToken = authSession.RequestToken;
        }

        public Task<HttpResponseMessage> SendAuthenticatedAsync(HttpClient client, HttpMethod method, string path)
        {
            if (_cookies is null || _antiforgeryHeaderName is null || _antiforgeryToken is null)
            {
                throw new InvalidOperationException("AuthenticateAsync must be called before SendAuthenticatedAsync.");
            }

            return SendAsync(client, method, path, _cookies, _antiforgeryHeaderName, _antiforgeryToken);
        }

        public async ValueTask DisposeAsync()
        {
            for (var i = _instances.Count - 1; i >= 0; i--)
            {
                await _instances[i].DisposeAsync();
            }

            _instances.Clear();
            await _postgres.DisposeAsync();
        }
    }
}
