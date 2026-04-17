namespace BuildingBlocks.Application.Dispatching.Behaviors;

internal sealed class CancellationBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return next(cancellationToken);
    }
}
