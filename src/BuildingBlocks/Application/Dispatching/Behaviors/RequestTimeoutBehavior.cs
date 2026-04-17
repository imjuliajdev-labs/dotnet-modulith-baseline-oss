using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching.Behaviors;

internal sealed class RequestTimeoutBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var timeout = request is ITimeoutRequest timeoutRequest
            ? timeoutRequest.Timeout
            : DefaultTimeout;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);

        try
        {
            return await next(linkedCts.Token);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var error = new Error("request.timeout", "The request exceeded the allowed execution time.", ErrorKind.Cancelled);
            return DispatcherResponseFactory.CreateFailure<TResponse>(error);
        }
    }
}
