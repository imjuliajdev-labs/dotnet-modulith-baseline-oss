using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching.Behaviors;

internal sealed class RequestExceptionBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
{
    private readonly IServiceProvider _serviceProvider;

    public RequestExceptionBehavior(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RequestExceptionResolver.ExecuteActionsAsync(_serviceProvider, request, exception, cancellationToken);

            var handledResponse = await RequestExceptionResolver.TryHandleAsync<TRequest, TResponse>(
                _serviceProvider,
                request,
                exception,
                cancellationToken);

            if (handledResponse.handled)
            {
                return handledResponse.response!;
            }

            var mapper = _serviceProvider.GetService(typeof(IExceptionToErrorMapper)) as IExceptionToErrorMapper;
            if (mapper is null)
            {
                throw;
            }

            var mappedError = mapper.Map(exception);
            if (mappedError == Error.None)
            {
                mappedError = new Error("exception.unmapped", "An unexpected error occurred.", ErrorKind.Failure);
            }

            return DispatcherResponseFactory.CreateFailure<TResponse>(mappedError);
        }
    }
}
