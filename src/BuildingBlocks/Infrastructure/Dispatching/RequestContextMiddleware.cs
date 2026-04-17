using BuildingBlocks.Application.Dispatching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Infrastructure.Dispatching;

public sealed class RequestContextMiddleware
{
    public const string CorrelationHeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public RequestContextMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext httpContext, IRequestContextAccessor requestContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(requestContextAccessor);

        var previous = requestContextAccessor.Current;
        var current = CreateForHttpRequest(httpContext);

        requestContextAccessor.Current = current;
        httpContext.Response.Headers[CorrelationHeaderName] = current.CorrelationId;

        try
        {
            await _next(httpContext);
        }
        finally
        {
            requestContextAccessor.Current = previous;
        }
    }

    internal static RequestContext CreateForHttpRequest(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var requestId = string.IsNullOrWhiteSpace(httpContext.TraceIdentifier)
            ? Guid.NewGuid().ToString("n")
            : httpContext.TraceIdentifier;

        var correlationId = httpContext.Request.Headers.TryGetValue(CorrelationHeaderName, out var values)
            ? values.ToString()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = requestId;
        }

        return new RequestContext(correlationId, requestId);
    }
}

public static class RequestContextApplicationBuilderExtensions
{
    public static IApplicationBuilder UseRequestContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<RequestContextMiddleware>();
    }
}
