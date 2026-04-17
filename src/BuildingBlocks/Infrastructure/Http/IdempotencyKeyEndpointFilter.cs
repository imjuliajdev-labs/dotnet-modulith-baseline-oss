using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Infrastructure.Http;

public sealed class IdempotencyKeyEndpointFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string HttpContextItemKey = "IdempotencyKey";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        var requestKey = httpContext.Request.Headers[HeaderName].ToString();

        httpContext.Items[HttpContextItemKey] = requestKey;

        return await next(context);
    }

    public static string GetRequestKey(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.Items.TryGetValue(HttpContextItemKey, out var value) && value is string key
            ? key
            : httpContext.Request.Headers[HeaderName].ToString();
    }
}

public static class IdempotencyKeyEndpointExtensions
{
    public static RouteHandlerBuilder WithIdempotencyKey(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilter<IdempotencyKeyEndpointFilter>();
        return builder;
    }
}
