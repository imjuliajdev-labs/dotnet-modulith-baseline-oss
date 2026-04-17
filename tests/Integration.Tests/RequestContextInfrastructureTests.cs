using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Dispatching;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

public sealed class RequestContextInfrastructureTests
{
    [Xunit.Fact]
    public async Task RequestContextMiddlewareUsesIncomingCorrelationIdAndRestoresAmbientState()
    {
        var services = new ServiceCollection();
        services.AddDispatcher();
        services.AddBuildingBlocksInfrastructureDefaults();

        await using var provider = services.BuildServiceProvider();

        var requestContextAccessor = provider.GetRequiredService<IRequestContextAccessor>();
        RequestContext? downstreamContext = null;

        var middleware = new RequestContextMiddleware(
            async httpContext =>
            {
                downstreamContext = requestContextAccessor.Current;
                await httpContext.Response.WriteAsync("ok");
            });

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            TraceIdentifier = "req-123"
        };

        httpContext.Response.Body = new MemoryStream();
        httpContext.Request.Headers[RequestContextMiddleware.CorrelationHeaderName] = "corr-456";

        await middleware.InvokeAsync(httpContext, requestContextAccessor);

        Xunit.Assert.NotNull(downstreamContext);
        Xunit.Assert.Equal("corr-456", downstreamContext!.CorrelationId);
        Xunit.Assert.Equal("req-123", downstreamContext.RequestId);
        Xunit.Assert.Equal("corr-456", httpContext.Response.Headers[RequestContextMiddleware.CorrelationHeaderName].ToString());
        Xunit.Assert.Null(requestContextAccessor.Current);
    }
}
