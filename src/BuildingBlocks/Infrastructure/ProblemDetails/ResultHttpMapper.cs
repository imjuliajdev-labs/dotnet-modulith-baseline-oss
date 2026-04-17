using BuildingBlocks.Application.ProblemDetails;
using BuildingBlocks.Application.Results;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Infrastructure.ProblemDetails;

public sealed class ResultHttpMapper
{
    private readonly IProblemDetailsMapper _problemDetailsMapper;
    private readonly ProblemDetailsHttpWriter _writer;

    public ResultHttpMapper(IProblemDetailsMapper problemDetailsMapper, ProblemDetailsHttpWriter writer)
    {
        _problemDetailsMapper = problemDetailsMapper ?? throw new ArgumentNullException(nameof(problemDetailsMapper));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public IResult Match(Result result, HttpContext httpContext, Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
        {
            return onSuccess();
        }

        return Failure(result.Error, httpContext);
    }

    public IResult Match<TValue>(Result<TValue> result, HttpContext httpContext, Func<TValue?, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        return Failure(result.Error, httpContext);
    }

    public IResult Failure(Error error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);

        var contract = _problemDetailsMapper.Map(error, httpContext.Request.Path.Value, httpContext.TraceIdentifier);
        return new ProblemDetailsHttpResult(contract, _writer);
    }
}
