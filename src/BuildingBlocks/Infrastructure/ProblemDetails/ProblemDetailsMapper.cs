using BuildingBlocks.Application.ProblemDetails;
using BuildingBlocks.Application.Results;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Infrastructure.ProblemDetails;

public sealed class ProblemDetailsMapper : IProblemDetailsMapper
{
    public ProblemDetailsContract Map(Error error, string? instance = null, string? traceId = null)
    {
        if (error == Error.None)
        {
            throw new ArgumentException("ProblemDetails cannot be created for the empty error sentinel.", nameof(error));
        }

        var code = string.IsNullOrWhiteSpace(error.Code)
            ? GetDefaultCode(error.Kind)
            : error.Code;

        var (status, title) = error.Kind switch
        {
            ErrorKind.Validation => (StatusCodes.Status400BadRequest, "Validation failed"),
            ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Resource not found"),
            ErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorKind.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ErrorKind.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            ErrorKind.ServiceUnavailable => (StatusCodes.Status503ServiceUnavailable, "Service unavailable"),
            ErrorKind.Cancelled => (499, "Request cancelled"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected failure")
        };

        return new ProblemDetailsContract(
            Type: $"urn:problem-type:{code}",
            Title: title,
            Status: status,
            Detail: string.IsNullOrWhiteSpace(error.Message) ? title : error.Message,
            Code: code,
            Instance: instance,
            TraceId: traceId);
    }

    private static string GetDefaultCode(ErrorKind kind)
    {
        return kind switch
        {
            ErrorKind.Validation => "validation.failed",
            ErrorKind.NotFound => "resource.not_found",
            ErrorKind.Conflict => "request.conflict",
            ErrorKind.Unauthorized => "auth.unauthorized",
            ErrorKind.Forbidden => "auth.forbidden",
            ErrorKind.ServiceUnavailable => "service.unavailable",
            ErrorKind.Cancelled => "request.cancelled",
            _ => "unexpected.failure"
        };
    }
}
