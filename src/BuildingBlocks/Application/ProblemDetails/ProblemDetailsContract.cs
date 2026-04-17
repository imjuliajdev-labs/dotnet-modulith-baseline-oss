using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.ProblemDetails;

public sealed record ProblemDetailsContract(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Code,
    string? Instance = null,
    string? TraceId = null);

public interface IProblemDetailsMapper
{
    ProblemDetailsContract Map(Error error, string? instance = null, string? traceId = null);
}
