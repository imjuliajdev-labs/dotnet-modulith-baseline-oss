namespace BuildingBlocks.Application.Dispatching.Behaviors;

internal sealed class RequestContextBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
{
    private readonly IRequestContextAccessor _requestContextAccessor;

    public RequestContextBehavior(IRequestContextAccessor requestContextAccessor)
    {
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_requestContextAccessor.Current is not null)
        {
            return await next(cancellationToken);
        }

        var previous = _requestContextAccessor.Current;
        _requestContextAccessor.Current = RequestContextFactory.CreateForBackgroundDispatch();

        try
        {
            return await next(cancellationToken);
        }
        finally
        {
            _requestContextAccessor.Current = previous;
        }
    }
}
