using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching;

internal sealed class NoOpRequestTelemetrySessionFactory : IRequestTelemetrySessionFactory
{
    public IRequestTelemetrySession Start(RequestTelemetryContext context)
    {
        return NoOpRequestTelemetrySession.Instance;
    }

    private sealed class NoOpRequestTelemetrySession : IRequestTelemetrySession
    {
        public static NoOpRequestTelemetrySession Instance { get; } = new();

        public void Complete()
        {
        }

        public void Fail(Error error)
        {
        }

        public void Fail(Exception exception)
        {
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }
}
