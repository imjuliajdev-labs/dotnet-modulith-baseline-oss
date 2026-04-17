using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching.Behaviors;

internal sealed class RequestTelemetryBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
{
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IRequestTelemetrySessionFactory _telemetrySessionFactory;

    public RequestTelemetryBehavior(
        IRequestContextAccessor requestContextAccessor,
        IRequestTelemetrySessionFactory telemetrySessionFactory)
    {
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _telemetrySessionFactory = telemetrySessionFactory ?? throw new ArgumentNullException(nameof(telemetrySessionFactory));
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestContext = _requestContextAccessor.Current ?? RequestContextFactory.CreateForBackgroundDispatch();
        using var session = _telemetrySessionFactory.Start(CreateContext(requestContext, request));

        try
        {
            var response = await next(cancellationToken);

            if (CommandTransactionRuntime.TryGetFailure(response, out var error))
            {
                session.Fail(error);
            }
            else
            {
                session.Complete();
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            session.Cancel();
            throw;
        }
        catch (Exception exception)
        {
            session.Fail(exception);
            throw;
        }
    }

    private static RequestTelemetryContext CreateContext(RequestContext requestContext, TRequest request)
    {
        var requestType = typeof(TRequest);
        var requestKind = CommandTransactionRuntime.IsCommandRequestType(requestType)
            ? "command"
            : "query";

        var moduleKey = request is IModuleScoped moduleScopedRequest
            ? moduleScopedRequest.ModuleKey
            : null;

        return new RequestTelemetryContext(
            requestType,
            requestKind,
            requestContext.CorrelationId,
            requestContext.RequestId,
            moduleKey);
    }
}
