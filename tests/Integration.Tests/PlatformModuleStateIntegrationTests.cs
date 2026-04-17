using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Dispatching;
using BuildingBlocks.Infrastructure.Modules;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Identity.Api.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Platform.Application.Auditing; // BP-031 dispatch-coverage marker
using Platform.Application.Health; // BP-031 dispatch-coverage marker
using Platform.Application.ModuleState;
using IClock = BuildingBlocks.Domain.Time.IClock;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests;

public sealed class PlatformModuleStateIntegrationTests
{
    [Xunit.Fact]
    public async Task PlatformModuleStateControlPlaneUsesLiveStateForEndpointAvailabilityAndAuditHistory()
    {
        await using var application = await CreateApplicationAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var unavailableBefore = await SendAsync(client, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(unavailableBefore, HttpStatusCode.ServiceUnavailable, "module.disabled");

        var listBefore = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/modules");
        Xunit.Assert.Equal(HttpStatusCode.OK, listBefore.StatusCode);

        using (var beforeJson = await ReadJsonAsync(listBefore))
        {
            var reports = GetModule(beforeJson, "reports");
            Xunit.Assert.False(reports.GetProperty("defaultEnabled").GetBoolean());
            Xunit.Assert.True(reports.GetProperty("canBeDisabled").GetBoolean());
            Xunit.Assert.Equal("disabled", reports.GetProperty("desiredState").GetString());
            Xunit.Assert.Equal("disabled", reports.GetProperty("runtimeState").GetString());
            Xunit.Assert.Equal(0, reports.GetProperty("version").GetInt64());
            Xunit.Assert.Equal(JsonValueKind.Null, reports.GetProperty("transitionId").ValueKind);
            Xunit.Assert.Equal(0, reports.GetProperty("changes").GetArrayLength());
        }

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var enableResponse = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        using (var enableJson = await ReadJsonAsync(enableResponse))
        {
            Xunit.Assert.Equal("reports", enableJson.RootElement.GetProperty("key").GetString());
            Xunit.Assert.Equal("enabled", enableJson.RootElement.GetProperty("desiredState").GetString());
            Xunit.Assert.Equal("enabled", enableJson.RootElement.GetProperty("runtimeState").GetString());
            Xunit.Assert.Equal(2, enableJson.RootElement.GetProperty("version").GetInt64());
            Xunit.Assert.Equal(JsonValueKind.String, enableJson.RootElement.GetProperty("transitionId").ValueKind);

            var changes = enableJson.RootElement.GetProperty("changes");
            Xunit.Assert.Equal(2, changes.GetArrayLength());

            var enableHistory = changes.EnumerateArray().ToArray();
            Xunit.Assert.Equal("enabling", enableHistory[0].GetProperty("beforeState").GetString());
            Xunit.Assert.Equal("enabled", enableHistory[0].GetProperty("afterState").GetString());
            Xunit.Assert.Equal("disabled", enableHistory[1].GetProperty("beforeState").GetString());
            Xunit.Assert.Equal("enabling", enableHistory[1].GetProperty("afterState").GetString());
            Xunit.Assert.Equal("identity:seeded-admin", enableHistory[0].GetProperty("actorId").GetString());
            Xunit.Assert.NotEqual(default, enableHistory[0].GetProperty("changedUtc").GetDateTimeOffset());
        }

        var availableAfterEnable = await SendAsync(client, HttpMethod.Get, "/api/v1/reports/status");
        Xunit.Assert.Equal(HttpStatusCode.OK, availableAfterEnable.StatusCode);

        using (var availableJson = await ReadJsonAsync(availableAfterEnable))
        {
            Xunit.Assert.Equal("online", availableJson.RootElement.GetProperty("status").GetString());
        }

        var disableResponse = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        using (var disableJson = await ReadJsonAsync(disableResponse))
        {
            Xunit.Assert.Equal("disabled", disableJson.RootElement.GetProperty("desiredState").GetString());
            Xunit.Assert.Equal("disabled", disableJson.RootElement.GetProperty("runtimeState").GetString());
            Xunit.Assert.Equal(4, disableJson.RootElement.GetProperty("version").GetInt64());
            Xunit.Assert.Equal(4, disableJson.RootElement.GetProperty("changes").GetArrayLength());
        }

        var unavailableAfterDisable = await SendAsync(client, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(unavailableAfterDisable, HttpStatusCode.ServiceUnavailable, "module.disabled");

        var listAfter = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/modules");
        Xunit.Assert.Equal(HttpStatusCode.OK, listAfter.StatusCode);

        using var afterJson = await ReadJsonAsync(listAfter);
        var reportsAfter = GetModule(afterJson, "reports");
        Xunit.Assert.Equal("disabled", reportsAfter.GetProperty("desiredState").GetString());
        Xunit.Assert.Equal("disabled", reportsAfter.GetProperty("runtimeState").GetString());
        Xunit.Assert.Equal(4, reportsAfter.GetProperty("version").GetInt64());

        var history = reportsAfter.GetProperty("changes").EnumerateArray().ToArray();
        Xunit.Assert.Equal(4, history.Length);
        Xunit.Assert.Equal("disabling", history[0].GetProperty("beforeState").GetString());
        Xunit.Assert.Equal("disabled", history[0].GetProperty("afterState").GetString());
        Xunit.Assert.Equal("enabled", history[1].GetProperty("beforeState").GetString());
        Xunit.Assert.Equal("disabling", history[1].GetProperty("afterState").GetString());
        Xunit.Assert.Equal("enabling", history[2].GetProperty("beforeState").GetString());
        Xunit.Assert.Equal("enabled", history[2].GetProperty("afterState").GetString());
        Xunit.Assert.Equal("disabled", history[3].GetProperty("beforeState").GetString());
        Xunit.Assert.Equal("enabling", history[3].GetProperty("afterState").GetString());
    }

    [Xunit.Fact]
    public async Task PlatformModuleStateExposesTransitionStatesWhileEnableAndDisableAreInFlight()
    {
        var transitionParticipant = new ReportsTransitionParticipant();

        await using var application = await CreateApplicationAsync(
            configureBuilder: builder => builder.Services.AddSingleton<IPlatformModuleTransitionParticipant>(transitionParticipant));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var enableTask = SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        await transitionParticipant.WaitForEnableEnteredAsync();

        var unavailableWhileEnabling = await SendAsync(client, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(unavailableWhileEnabling, HttpStatusCode.ServiceUnavailable, "module.enabling");

        var moduleListWhileEnabling = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/modules");
        Xunit.Assert.Equal(HttpStatusCode.OK, moduleListWhileEnabling.StatusCode);

        using (var enablingJson = await ReadJsonAsync(moduleListWhileEnabling))
        {
            var reports = GetModule(enablingJson, "reports");
            Xunit.Assert.Equal("enabled", reports.GetProperty("desiredState").GetString());
            Xunit.Assert.Equal("enabling", reports.GetProperty("runtimeState").GetString());
            Xunit.Assert.Equal(JsonValueKind.String, reports.GetProperty("transitionId").ValueKind);
        }

        transitionParticipant.ReleaseEnable();

        var enableResponse = await enableTask;
        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var disableTask = SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        await transitionParticipant.WaitForDisableEnteredAsync();

        var unavailableWhileDisabling = await SendAsync(client, HttpMethod.Get, "/api/v1/reports/status");
        await AssertProblemAsync(unavailableWhileDisabling, HttpStatusCode.ServiceUnavailable, "module.disabling");

        var moduleListWhileDisabling = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/modules");
        Xunit.Assert.Equal(HttpStatusCode.OK, moduleListWhileDisabling.StatusCode);

        using (var disablingJson = await ReadJsonAsync(moduleListWhileDisabling))
        {
            var reports = GetModule(disablingJson, "reports");
            Xunit.Assert.Equal("disabled", reports.GetProperty("desiredState").GetString());
            Xunit.Assert.Equal("disabling", reports.GetProperty("runtimeState").GetString());
            Xunit.Assert.Equal(JsonValueKind.String, reports.GetProperty("transitionId").ValueKind);
        }

        transitionParticipant.ReleaseDisable();

        var disableResponse = await disableTask;
        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
    }

    [Xunit.Fact]
    public async Task PlatformModuleStateSerializesConcurrentMutationsPerModule()
    {
        var transitionParticipant = new ReportsTransitionParticipant();

        await using var application = await CreateApplicationAsync(
            configureBuilder: builder => builder.Services.AddSingleton<IPlatformModuleTransitionParticipant>(transitionParticipant));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var enableTask = SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        await transitionParticipant.WaitForEnableEnteredAsync();

        var disableTask = SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        var completedBeforeRelease = await Task.WhenAny(disableTask, Task.Delay(TimeSpan.FromMilliseconds(750))) == disableTask;
        Xunit.Assert.False(completedBeforeRelease);

        transitionParticipant.ReleaseEnable();

        var enableResponse = await enableTask;
        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        await transitionParticipant.WaitForDisableEnteredAsync();
        transitionParticipant.ReleaseDisable();

        var disableResponse = await disableTask;
        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
    }

    [Xunit.Fact]
    public async Task PlatformModuleStateMutationsReturnCanonicalFailuresForInvalidTransitions()
    {
        await using var application = await CreateApplicationAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var alreadyDisabled = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);
        await AssertProblemAsync(alreadyDisabled, HttpStatusCode.Conflict, "platform.module_state.already_disabled");

        var enableResponse = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable",
            cookies,
            session.HeaderName,
            session.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var alreadyEnabled = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable",
            cookies,
            session.HeaderName,
            session.RequestToken);
        await AssertProblemAsync(alreadyEnabled, HttpStatusCode.Conflict, "platform.module_state.already_enabled");

        var criticalModule = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/platform/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);
        await AssertProblemAsync(criticalModule, HttpStatusCode.Conflict, "platform.module_state.cannot_disable");

        var unknownModule = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/does-not-exist/enable",
            cookies,
            session.HeaderName,
            session.RequestToken);
        await AssertProblemAsync(unknownModule, HttpStatusCode.NotFound, "platform.module_state.unknown_module");
    }

    [Xunit.Fact]
    public async Task PlatformModuleStateWriteEndpointsReturnUnauthorizedForAnonymousCallers()
    {
        await using var application = await CreateApplicationAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var antiforgery = await GetAntiforgeryAsync(client);
        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable",
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthorized");
    }

    [Xunit.Fact]
    public async Task PlatformModuleStateWriteEndpointsRequireAntiforgeryForAuthenticatedCallers()
    {
        await using var application = await CreateApplicationAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var signInResult = await SignInAsync(client);


        var authCookie = signInResult.Cookie;

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable",
            authCookie);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "security.antiforgery_invalid");
    }

    [Xunit.Fact]
    public async Task PlatformModuleStateWriteEndpointsRequireRecentAuthentication()
    {
        var clock = new AdjustableClock(SystemClock.Instance.GetCurrentInstant());

        await using var application = await CreateApplicationAsync(
            configureBuilder: builder => builder.Services.AddSingleton<IClock>(clock));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var signInResult = await SignInAsync(client);


        var authCookie = signInResult.Cookie;

        clock.Advance(Duration.FromMinutes(6));

        var authenticatedAntiforgery = await GetAntiforgeryAsync(client, authCookie);
        var cookies = CombineCookies(authenticatedAntiforgery.Cookie, authCookie);

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable",
            cookies,
            authenticatedAntiforgery.HeaderName,
            authenticatedAntiforgery.RequestToken);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.recent_auth_required");
    }

    [Xunit.Fact]
    public async Task PlatformHealthSummaryTracksLiveModuleAvailability()
    {
        await using var application = await CreateApplicationAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var before = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/health", cookies);
        Xunit.Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using (var beforeJson = await ReadJsonAsync(before))
        {
            Xunit.Assert.Equal("degraded", beforeJson.RootElement.GetProperty("overallStatus").GetString());
            Xunit.Assert.Equal(1, beforeJson.RootElement.GetProperty("disabledModules").GetInt32());
        }

        var enable = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable",
            cookies,
            session.HeaderName,
            session.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

        var after = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/health", cookies);
        Xunit.Assert.Equal(HttpStatusCode.OK, after.StatusCode);

        using var afterJson = await ReadJsonAsync(after);
        Xunit.Assert.Equal("healthy", afterJson.RootElement.GetProperty("overallStatus").GetString());
        Xunit.Assert.Equal(0, afterJson.RootElement.GetProperty("disabledModules").GetInt32());
    }

    [Xunit.Fact]
    public async Task PlatformAuditEndpointReturnsRecordedModuleStateMutations()
    {
        await using var application = await CreateApplicationAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

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

        var disable = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        var audit = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/audit-events?limit=2", cookies);
        Xunit.Assert.Equal(HttpStatusCode.OK, audit.StatusCode);

        using var auditJson = await ReadJsonAsync(audit);
        var events = auditJson.RootElement.GetProperty("events").EnumerateArray().ToArray();
        Xunit.Assert.Equal(2, events.Length);
        Xunit.Assert.Equal("platform.module_state.disable", events[0].GetProperty("action").GetString());
        Xunit.Assert.Equal("reports", events[0].GetProperty("targetId").GetString());
        Xunit.Assert.Equal("identity:seeded-admin", events[0].GetProperty("actorId").GetString());
        Xunit.Assert.False(string.IsNullOrWhiteSpace(events[0].GetProperty("correlationId").GetString()));
        Xunit.Assert.Equal("platform.module_state.enable", events[1].GetProperty("action").GetString());
    }

    [Xunit.Fact]
    public async Task PlatformModuleStateWriteEndpointsAreRateLimited()
    {
        await using var application = await CreateApplicationAsync(moduleStateMutationPermitLimit: 1);

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var first = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);

        var second = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Xunit.Fact]
    public async Task HostHealthEndpointIsAvailable()
    {
        await using var application = await CreateApplicationAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var response = await SendAsync(client, HttpMethod.Get, "/health");

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<PostgresBackedApiApplication> CreateApplicationAsync(
        IReadOnlyDictionary<string, string?>? overrides = null,
        int moduleStateMutationPermitLimit = 20,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        return PostgresBackedApiApplication.StartAsync(
            configurationOverrides: overrides,
            moduleStateMutationPermitLimit: moduleStateMutationPermitLimit,
            configureBuilder: builder =>
            {
                builder.Services.AddApiModule<ReportsModule>();
                configureBuilder?.Invoke(builder);
            });
    }
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode, string expectedCode)
    {
        Xunit.Assert.Equal(expectedStatusCode, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
    }

    private static JsonElement GetModule(JsonDocument document, string moduleKey)
    {
        return document.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .Single(element => string.Equals(element.GetProperty("key").GetString(), moduleKey, StringComparison.Ordinal));
    }

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

    public sealed class ReportsModule : IApiModule
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
                .WithName("Reports_GetStatus");
        }
    }

    public sealed record GetReportsStatusQuery : IQuery<string>, IModuleScoped
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

    private sealed record ReportsStatusResponse(string Status);
}
