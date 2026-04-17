using System.Security.Claims;
using BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Architecture.Tests;

public sealed class BrowserMutationAntiforgeryMiddlewareTests
{
    [Fact]
    public async Task BrowserRequestsToApiMutationsAreValidated()
    {
        var antiforgery = new RecordingAntiforgery();
        var nextCalled = false;
        var middleware = new BrowserMutationAntiforgeryMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var httpContext = CreateHttpContext(path: "/api/v1/blog/posts", method: HttpMethods.Post);

        await middleware.InvokeAsync(httpContext, antiforgery);

        Assert.Equal(1, antiforgery.ValidateCallCount);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task SafeMethodsSkipValidation()
    {
        var antiforgery = new RecordingAntiforgery();
        var middleware = new BrowserMutationAntiforgeryMiddleware(_ => Task.CompletedTask);
        var httpContext = CreateHttpContext(path: "/api/v1/blog/posts", method: HttpMethods.Get);

        await middleware.InvokeAsync(httpContext, antiforgery);

        Assert.Equal(0, antiforgery.ValidateCallCount);
    }

    [Fact]
    public async Task NonApiMutationsSkipValidation()
    {
        var antiforgery = new RecordingAntiforgery();
        var middleware = new BrowserMutationAntiforgeryMiddleware(_ => Task.CompletedTask);
        var httpContext = CreateHttpContext(path: "/health", method: HttpMethods.Post);

        await middleware.InvokeAsync(httpContext, antiforgery);

        Assert.Equal(0, antiforgery.ValidateCallCount);
    }

    [Fact]
    public async Task MachineAuthenticatedApiMutationsAreExempt()
    {
        var antiforgery = new RecordingAntiforgery();
        var middleware = new BrowserMutationAntiforgeryMiddleware(_ => Task.CompletedTask);
        var httpContext = CreateHttpContext(path: "/api/v1/admin/announcements", method: HttpMethods.Post);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Machine"));

        await middleware.InvokeAsync(httpContext, antiforgery);

        Assert.Equal(0, antiforgery.ValidateCallCount);
    }

    private static DefaultHttpContext CreateHttpContext(string path, string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        return new DefaultHttpContext
        {
            Request =
            {
                Path = path,
                Method = method
            }
        };
    }

    private sealed class RecordingAntiforgery : IAntiforgery
    {
        public int ValidateCallCount { get; private set; }

        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext)
            => throw new NotSupportedException();

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext)
            => throw new NotSupportedException();

        public Task<bool> IsRequestValidAsync(HttpContext httpContext)
            => Task.FromResult(true);

        public Task ValidateRequestAsync(HttpContext httpContext)
        {
            ValidateCallCount++;
            return Task.CompletedTask;
        }

        public void SetCookieTokenAndHeader(HttpContext httpContext)
            => throw new NotSupportedException();
    }
}
