using BuildingBlocks.Application.ProblemDetails;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Infrastructure.ProblemDetails;

internal sealed class ProblemDetailsHttpResult : IResult
{
    private readonly ProblemDetailsContract _contract;
    private readonly ProblemDetailsHttpWriter _writer;

    public ProblemDetailsHttpResult(ProblemDetailsContract contract, ProblemDetailsHttpWriter writer)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        return _writer.WriteAsync(httpContext, _contract, httpContext.RequestAborted);
    }
}
