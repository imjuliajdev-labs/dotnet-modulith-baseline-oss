using System.Diagnostics;
using System.Threading;

namespace BuildingBlocks.Application.Dispatching;

public sealed record RequestContext(string CorrelationId, string RequestId);

public interface IRequestContextAccessor
{
    RequestContext? Current { get; set; }
}

internal sealed class AsyncLocalRequestContextAccessor : IRequestContextAccessor
{
    private readonly AsyncLocal<RequestContext?> _current = new();

    public RequestContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

internal static class RequestContextFactory
{
    public static RequestContext CreateForBackgroundDispatch()
    {
        var requestId = Activity.Current?.Id;
        if (string.IsNullOrWhiteSpace(requestId))
        {
            requestId = Guid.NewGuid().ToString("n");
        }

        var correlationId = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = requestId;
        }

        return new RequestContext(correlationId, requestId);
    }
}
