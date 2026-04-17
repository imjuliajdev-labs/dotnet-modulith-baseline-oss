using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching;

internal sealed class Dispatcher : IDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    private delegate Task<TResponse> HandlerInvoker<TResponse>(object handler, object request, CancellationToken cancellationToken);
    private delegate Task<TResponse> BehaviorInvoker<TResponse>(object behavior, object request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);

    // The behaviors in the pipeline own the effective cancellation token passed to the next stage. The dispatcher does not
    // close over the caller token when building `next`; each behavior decides whether to forward the inbound token unchanged
    // or substitute a linked token (e.g., RequestTimeoutBehavior). See ADR-CUSTOM-DISPATCHER "Cancellation contract".

    public Dispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public Task<Result> Send(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var requestType = command.GetType();
        var handlerServiceType = typeof(ICommandHandler<>).MakeGenericType(requestType);

        return Dispatch<Result>(command, handlerServiceType, cancellationToken);
    }

    public Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var requestType = command.GetType();
        var handlerServiceType = typeof(ICommandHandler<,>).MakeGenericType(requestType, typeof(TResponse));

        return Dispatch<Result<TResponse>>(command, handlerServiceType, cancellationToken);
    }

    public Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requestType = query.GetType();
        var handlerServiceType = typeof(IQueryHandler<,>).MakeGenericType(requestType, typeof(TResponse));

        return Dispatch<Result<TResponse>>(query, handlerServiceType, cancellationToken);
    }

    private async Task<TResponse> Dispatch<TResponse>(object request, Type handlerServiceType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestType = request.GetType();
        var handler = _serviceProvider.GetService(handlerServiceType)
            ?? throw new InvalidOperationException(
                $"No dispatcher handler is registered for request {requestType.FullName}. Ensure the owning assembly is added through AddDispatcher or the handler is registered manually.");

        var handlerInvoker = HandlerInvokerCache<TResponse>.GetOrAdd(handler.GetType(), requestType);

        RequestHandlerDelegate<TResponse> next = ct => handlerInvoker(handler, request, ct);

        var pipelineBehaviorType = typeof(IRequestPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var pipelineBehaviors = ResolveServices(pipelineBehaviorType).ToArray();

        foreach (var behavior in pipelineBehaviors.Reverse())
        {
            var currentNext = next;
            var behaviorInvoker = BehaviorInvokerCache<TResponse>.GetOrAdd(behavior.GetType(), requestType);

            next = ct => behaviorInvoker(behavior, request, currentNext, ct);
        }

        return await next(cancellationToken);
    }

    private IEnumerable<object> ResolveServices(Type serviceType)
    {
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
        return (IEnumerable<object>?)_serviceProvider.GetService(enumerableType) ?? Array.Empty<object>();
    }

    private static class HandlerInvokerCache<TResponse>
    {
        private static readonly ConcurrentDictionary<(Type ImplementationType, Type RequestType), HandlerInvoker<TResponse>> Cache = new();

        public static HandlerInvoker<TResponse> GetOrAdd(Type implementationType, Type requestType)
        {
            return Cache.GetOrAdd(
                (implementationType, requestType),
                static key => Create(key.ImplementationType, key.RequestType));
        }

        private static HandlerInvoker<TResponse> Create(Type implementationType, Type requestType)
        {
            var handlerMethod = implementationType.GetMethod(
                                    nameof(ICommandHandler<ICommand>.Handle),
                                    BindingFlags.Instance | BindingFlags.Public,
                                    binder: null,
                                    types: new[] { requestType, typeof(CancellationToken) },
                                    modifiers: null)
                                ?? throw new InvalidOperationException(
                                    $"Handler {implementationType.FullName} does not expose a compatible Handle method.");

            var handlerParameter = Expression.Parameter(typeof(object), "handler");
            var requestParameter = Expression.Parameter(typeof(object), "request");
            var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
            var call = Expression.Call(
                Expression.Convert(handlerParameter, implementationType),
                handlerMethod,
                Expression.Convert(requestParameter, requestType),
                cancellationTokenParameter);

            if (call.Type != typeof(Task<TResponse>))
            {
                throw new InvalidOperationException($"Handler {implementationType.FullName} returned an unexpected result.");
            }

            return Expression.Lambda<HandlerInvoker<TResponse>>(
                call,
                handlerParameter,
                requestParameter,
                cancellationTokenParameter).Compile();
        }
    }

    private static class BehaviorInvokerCache<TResponse>
    {
        private static readonly ConcurrentDictionary<(Type ImplementationType, Type RequestType), BehaviorInvoker<TResponse>> Cache = new();

        public static BehaviorInvoker<TResponse> GetOrAdd(Type implementationType, Type requestType)
        {
            return Cache.GetOrAdd(
                (implementationType, requestType),
                static key => Create(key.ImplementationType, key.RequestType));
        }

        private static BehaviorInvoker<TResponse> Create(Type implementationType, Type requestType)
        {
            var behaviorMethod = implementationType.GetMethod(
                                     nameof(IRequestPipelineBehavior<object, object>.Handle),
                                     BindingFlags.Instance | BindingFlags.Public,
                                     binder: null,
                                     types: new[]
                                     {
                                         requestType,
                                         typeof(RequestHandlerDelegate<>).MakeGenericType(typeof(TResponse)),
                                         typeof(CancellationToken)
                                     },
                                     modifiers: null)
                                 ?? throw new InvalidOperationException(
                                     $"Pipeline behavior {implementationType.FullName} does not expose a compatible Handle method.");

            var behaviorParameter = Expression.Parameter(typeof(object), "behavior");
            var requestParameter = Expression.Parameter(typeof(object), "request");
            var nextParameter = Expression.Parameter(typeof(RequestHandlerDelegate<TResponse>), "next");
            var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
            var call = Expression.Call(
                Expression.Convert(behaviorParameter, implementationType),
                behaviorMethod,
                Expression.Convert(requestParameter, requestType),
                nextParameter,
                cancellationTokenParameter);

            if (call.Type != typeof(Task<TResponse>))
            {
                throw new InvalidOperationException($"Pipeline behavior {implementationType.FullName} returned an unexpected result.");
            }

            return Expression.Lambda<BehaviorInvoker<TResponse>>(
                call,
                behaviorParameter,
                requestParameter,
                nextParameter,
                cancellationTokenParameter).Compile();
        }
    }
}
