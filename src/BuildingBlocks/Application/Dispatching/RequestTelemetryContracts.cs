using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching;

public sealed record RequestTelemetryContext(
    Type RequestType,
    string RequestKind,
    string CorrelationId,
    string RequestId,
    string? ModuleKey);

public interface IRequestTelemetrySessionFactory
{
    IRequestTelemetrySession Start(RequestTelemetryContext context);
}

public interface IRequestTelemetrySession : IDisposable
{
    void Complete();

    void Fail(Error error);

    void Fail(Exception exception);

    void Cancel();
}
