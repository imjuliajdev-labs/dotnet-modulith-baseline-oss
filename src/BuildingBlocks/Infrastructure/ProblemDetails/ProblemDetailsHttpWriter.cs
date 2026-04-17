using BuildingBlocks.Application.ProblemDetails;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Infrastructure.ProblemDetails;

public sealed class ProblemDetailsHttpWriter
{
    public Task WriteAsync(HttpContext httpContext, ProblemDetailsContract contract, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(contract);

        var payload = contract with
        {
            Instance = string.IsNullOrWhiteSpace(contract.Instance) ? httpContext.Request.Path.Value : contract.Instance,
            TraceId = string.IsNullOrWhiteSpace(contract.TraceId) ? httpContext.TraceIdentifier : contract.TraceId
        };

        httpContext.Response.StatusCode = payload.Status;
        httpContext.Response.ContentType = "application/problem+json";

        return httpContext.Response.WriteAsJsonAsync(
            payload,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
    }
}
