using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.ProblemDetails;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Infrastructure.ProblemDetails;

public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly IExceptionToErrorMapper _exceptionToErrorMapper;
    private readonly IProblemDetailsMapper _problemDetailsMapper;
    private readonly ProblemDetailsHttpWriter _writer;

    public ProblemDetailsExceptionHandler(
        IExceptionToErrorMapper exceptionToErrorMapper,
        IProblemDetailsMapper problemDetailsMapper,
        ProblemDetailsHttpWriter writer)
    {
        _exceptionToErrorMapper = exceptionToErrorMapper ?? throw new ArgumentNullException(nameof(exceptionToErrorMapper));
        _problemDetailsMapper = problemDetailsMapper ?? throw new ArgumentNullException(nameof(problemDetailsMapper));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (httpContext.Response.HasStarted || exception is OperationCanceledException)
        {
            return false;
        }

        var error = _exceptionToErrorMapper.Map(exception);
        var contract = _problemDetailsMapper.Map(error, httpContext.Request.Path.Value, httpContext.TraceIdentifier);

        await _writer.WriteAsync(httpContext, contract, cancellationToken);
        return true;
    }
}
