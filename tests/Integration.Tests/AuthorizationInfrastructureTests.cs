using System.Security.Claims;
using System.Text.Json;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Authorization;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using System.Globalization;
using IClock = BuildingBlocks.Domain.Time.IClock;

namespace Integration.Tests;

public sealed class AuthorizationInfrastructureTests
{
    [Xunit.Fact]
    public async Task BuildingBlocksInfrastructureDefaultsReadCurrentActorFromHttpContextClaims()
    {
        var authenticatedAt = Instant.FromUtc(2026, 4, 4, 12, 0);

        var services = new ServiceCollection();
        services.AddDispatcher();
        services.AddBuildingBlocksInfrastructureDefaults();

        await using var provider = services.BuildServiceProvider();

        var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
        var currentActorAccessor = provider.GetRequiredService<ICurrentActorAccessor>();

        httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = CreateAuthenticatedPrincipal(
                "user-123",
                [
                    new Claim(
                        HttpContextCurrentActorAccessor.AuthenticationInstantClaimType,
                        authenticatedAt.ToUnixTimeTicks().ToString(CultureInfo.InvariantCulture)),
                    new Claim(ClaimTypes.Role, IdentityRoles.Admin),
                    new Claim(ClaimTypes.Role, IdentityRoles.User)
                ])
        };

        var actor = await currentActorAccessor.GetCurrentAsync(CancellationToken.None);

        Xunit.Assert.True(actor.IsAuthenticated);
        Xunit.Assert.Equal("user-123", actor.ActorId);
        Xunit.Assert.Equal(authenticatedAt, actor.AuthenticatedAt);
        Xunit.Assert.True(actor.IsInRole(IdentityRoles.Admin));
        Xunit.Assert.True(actor.IsInRole(IdentityRoles.User));
        Xunit.Assert.False(actor.IsInRole(IdentityRoles.Machine));
    }

    [Xunit.Fact]
    public async Task DispatcherAuthorizationFailuresMapToSharedProblemDetailsPayloads()
    {
        var services = new ServiceCollection();
        services.AddDispatcher();
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddScoped<ICommandHandler<HttpProtectedCommand, string>, HttpProtectedCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var mapper = scope.ServiceProvider.GetRequiredService<ResultHttpMapper>();

        var unauthorizedContext = CreateHttpContext(scope.ServiceProvider, "/api/v1/admin/protected");
        unauthorizedContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        httpContextAccessor.HttpContext = unauthorizedContext;

        var unauthorized = await dispatcher.Send(new HttpProtectedCommand("admin", "request-1", "blocked"));
        await mapper.Match(unauthorized, unauthorizedContext, _ => new TestHttpResult(StatusCodes.Status204NoContent, "ok"))
            .ExecuteAsync(unauthorizedContext);

        Xunit.Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedContext.Response.StatusCode);
        Xunit.Assert.Equal("application/problem+json", unauthorizedContext.Response.ContentType);

        using var unauthorizedJson = await ReadJsonAsync(unauthorizedContext);
        Xunit.Assert.Equal("auth.unauthorized", unauthorizedJson.RootElement.GetProperty("code").GetString());
        Xunit.Assert.Equal("Unauthorized", unauthorizedJson.RootElement.GetProperty("title").GetString());

        var forbiddenContext = CreateHttpContext(scope.ServiceProvider, "/api/v1/admin/protected");
        forbiddenContext.User = CreateAuthenticatedPrincipal(
            "user-123",
            [new Claim(ClaimTypes.Role, IdentityRoles.User)]);
        httpContextAccessor.HttpContext = forbiddenContext;

        var forbidden = await dispatcher.Send(new HttpProtectedCommand("admin", "request-2", "blocked"));
        await mapper.Match(forbidden, forbiddenContext, _ => new TestHttpResult(StatusCodes.Status204NoContent, "ok"))
            .ExecuteAsync(forbiddenContext);

        Xunit.Assert.Equal(StatusCodes.Status403Forbidden, forbiddenContext.Response.StatusCode);
        Xunit.Assert.Equal("application/problem+json", forbiddenContext.Response.ContentType);

        using var forbiddenJson = await ReadJsonAsync(forbiddenContext);
        Xunit.Assert.Equal("auth.forbidden", forbiddenJson.RootElement.GetProperty("code").GetString());
        Xunit.Assert.Equal("Forbidden", forbiddenJson.RootElement.GetProperty("title").GetString());
    }

    [Xunit.Fact]
    public async Task DispatcherRecentAuthenticationFailuresMapToSharedProblemDetailsPayloads()
    {
        var clock = new AdjustableClock(Instant.FromUtc(2026, 4, 4, 12, 0));
        var staleAuthenticatedAt = clock.GetCurrentInstant() - Duration.FromMinutes(10);

        var services = new ServiceCollection();
        services.AddSingleton<IClock>(clock);
        services.AddDispatcher();
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddScoped<ICommandHandler<HttpRecentAuthProtectedCommand, string>, HttpRecentAuthProtectedCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var mapper = scope.ServiceProvider.GetRequiredService<ResultHttpMapper>();

        var httpContext = CreateHttpContext(scope.ServiceProvider, "/api/v1/admin/protected");
        httpContext.User = CreateAuthenticatedPrincipal(
            "user-123",
            [
                new Claim(
                    HttpContextCurrentActorAccessor.AuthenticationInstantClaimType,
                    staleAuthenticatedAt.ToUnixTimeTicks().ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Role, IdentityRoles.Admin)
            ]);
        httpContextAccessor.HttpContext = httpContext;

        var result = await dispatcher.Send(new HttpRecentAuthProtectedCommand("admin", "request-3", "blocked"));
        await mapper.Match(result, httpContext, _ => new TestHttpResult(StatusCodes.Status204NoContent, "ok"))
            .ExecuteAsync(httpContext);

        Xunit.Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Xunit.Assert.Equal("application/problem+json", httpContext.Response.ContentType);

        using var json = await ReadJsonAsync(httpContext);
        Xunit.Assert.Equal("auth.recent_auth_required", json.RootElement.GetProperty("code").GetString());
        Xunit.Assert.Equal("Unauthorized", json.RootElement.GetProperty("title").GetString());
    }

    public sealed record HttpProtectedCommand(string ModuleKey, string RequestKey, string Value)
        : IIdempotentCommand<string>, IModuleScoped, IAuthorizeRequest
    {
        public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
            new[] { RoleRequirement.Admin };
    }

    public sealed record HttpRecentAuthProtectedCommand(string ModuleKey, string RequestKey, string Value)
        : IIdempotentCommand<string>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
    {
        public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
            new[] { RoleRequirement.Admin };

        public Duration RecentAuthenticationWindow { get; } = Duration.FromMinutes(5);
    }

    private sealed class HttpProtectedCommandHandler : ICommandHandler<HttpProtectedCommand, string>
    {
        public Task<Result<string>> Handle(HttpProtectedCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    private sealed class HttpRecentAuthProtectedCommandHandler : ICommandHandler<HttpRecentAuthProtectedCommand, string>
    {
        public Task<Result<string>> Handle(HttpRecentAuthProtectedCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    private static ClaimsPrincipal CreateAuthenticatedPrincipal(string actorId, IEnumerable<Claim> additionalClaims)
    {
        var claims = new List<Claim>(additionalClaims)
        {
            new(ClaimTypes.NameIdentifier, actorId)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Cookies"));
    }

    private static DefaultHttpContext CreateHttpContext(IServiceProvider serviceProvider, PathString path)
    {
        return new DefaultHttpContext
        {
            RequestServices = serviceProvider,
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

    private sealed class TestHttpResult : IResult
    {
        private readonly string _body;
        private readonly int _statusCode;

        public TestHttpResult(int statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = _statusCode;
            await httpContext.Response.WriteAsync(_body);
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
