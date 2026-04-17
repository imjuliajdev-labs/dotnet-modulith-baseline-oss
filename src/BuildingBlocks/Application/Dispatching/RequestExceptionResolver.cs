using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace BuildingBlocks.Application.Dispatching;

internal static class RequestExceptionResolver
{
    private static readonly ConcurrentDictionary<(Type ImplementationType, Type RequestType, Type ExceptionType), ExceptionActionInvoker> ActionInvokerCache = new();
    private static readonly ConcurrentDictionary<(Type ImplementationType, Type RequestType, Type ResponseType, Type ExceptionType), ExceptionHandlerInvoker> HandlerInvokerCache = new();

    private delegate Task ExceptionActionInvoker(object action, object request, Exception exception, CancellationToken cancellationToken);
    private delegate Task<object?> ExceptionHandlerInvoker(object handler, object request, Exception exception, CancellationToken cancellationToken);

    public static async Task ExecuteActionsAsync<TRequest>(
        IServiceProvider serviceProvider,
        TRequest request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        foreach (var exceptionType in EnumerateExceptionTypes(exception.GetType()))
        {
            var actionServiceType = typeof(IRequestExceptionAction<,>).MakeGenericType(typeof(TRequest), exceptionType);
            var actions = ResolveServices(serviceProvider, actionServiceType).ToArray();

            foreach (var action in actions)
            {
                var invoker = ActionInvokerCache.GetOrAdd(
                    (action.GetType(), typeof(TRequest), exceptionType),
                    static key => CreateActionInvoker(key.ImplementationType, key.RequestType, key.ExceptionType));

                await invoker(action, request!, exception, cancellationToken);
            }
        }
    }

    public static async Task<(bool handled, TResponse? response)> TryHandleAsync<TRequest, TResponse>(
        IServiceProvider serviceProvider,
        TRequest request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        foreach (var exceptionType in EnumerateExceptionTypes(exception.GetType()))
        {
            var handlerServiceType = typeof(IRequestExceptionHandler<,,>).MakeGenericType(typeof(TRequest), typeof(TResponse), exceptionType);
            var handlers = ResolveServices(serviceProvider, handlerServiceType).ToArray();

            if (handlers.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple request exception handlers are registered for {handlerServiceType.FullName}. Register a single handler per request, response, and exception type.");
            }

            if (handlers.Length == 0)
            {
                continue;
            }

            var handler = handlers[0];
            var invoker = HandlerInvokerCache.GetOrAdd(
                (handler.GetType(), typeof(TRequest), typeof(TResponse), exceptionType),
                static key => CreateHandlerInvoker(key.ImplementationType, key.RequestType, key.ResponseType, key.ExceptionType));

            var response = await invoker(handler, request!, exception, cancellationToken);
            return (true, (TResponse?)response);
        }

        return (false, default);
    }

    private static ExceptionActionInvoker CreateActionInvoker(Type implementationType, Type requestType, Type exceptionType)
    {
        var executeMethod = implementationType.GetMethod(
            nameof(IRequestExceptionAction<object, Exception>.Execute),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [requestType, exceptionType, typeof(CancellationToken)],
            modifiers: null)
            ?? throw new InvalidOperationException(
                $"Request exception action {implementationType.FullName} does not expose a compatible Execute method.");

        var actionParameter = Expression.Parameter(typeof(object), "action");
        var requestParameter = Expression.Parameter(typeof(object), "request");
        var exceptionParameter = Expression.Parameter(typeof(Exception), "exception");
        var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var call = Expression.Call(
            Expression.Convert(actionParameter, implementationType),
            executeMethod,
            Expression.Convert(requestParameter, requestType),
            Expression.Convert(exceptionParameter, exceptionType),
            cancellationTokenParameter);

        if (call.Type != typeof(Task))
        {
            throw new InvalidOperationException($"Request exception action {implementationType.FullName} returned an unexpected result.");
        }

        return Expression.Lambda<ExceptionActionInvoker>(
            call,
            actionParameter,
            requestParameter,
            exceptionParameter,
            cancellationTokenParameter).Compile();
    }

    private static ExceptionHandlerInvoker CreateHandlerInvoker(Type implementationType, Type requestType, Type responseType, Type exceptionType)
    {
        var handleMethod = implementationType.GetMethod(
            nameof(IRequestExceptionHandler<object, object, Exception>.Handle),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [requestType, exceptionType, typeof(CancellationToken)],
            modifiers: null)
            ?? throw new InvalidOperationException(
                $"Request exception handler {implementationType.FullName} does not expose a compatible Handle method.");

        var handlerParameter = Expression.Parameter(typeof(object), "handler");
        var requestParameter = Expression.Parameter(typeof(object), "request");
        var exceptionParameter = Expression.Parameter(typeof(Exception), "exception");
        var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var call = Expression.Call(
            Expression.Convert(handlerParameter, implementationType),
            handleMethod,
            Expression.Convert(requestParameter, requestType),
            Expression.Convert(exceptionParameter, exceptionType),
            cancellationTokenParameter);

        var expectedTaskType = typeof(Task<>).MakeGenericType(responseType);
        if (call.Type != expectedTaskType)
        {
            throw new InvalidOperationException($"Request exception handler {implementationType.FullName} returned an unexpected result.");
        }

        var boxResultMethod = typeof(RequestExceptionResolver)
            .GetMethod(nameof(BoxResultAsync), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(responseType);
        var boxedCall = Expression.Call(boxResultMethod, call);

        return Expression.Lambda<ExceptionHandlerInvoker>(
            boxedCall,
            handlerParameter,
            requestParameter,
            exceptionParameter,
            cancellationTokenParameter).Compile();
    }

    private static async Task<object?> BoxResultAsync<TResponse>(Task<TResponse> task)
    {
        return await task;
    }

    private static IEnumerable<Type> EnumerateExceptionTypes(Type exceptionType)
    {
        for (var current = exceptionType; current is not null && current != typeof(object); current = current.BaseType)
        {
            yield return current;
        }
    }

    private static IEnumerable<object> ResolveServices(IServiceProvider serviceProvider, Type serviceType)
    {
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
        return (IEnumerable<object>?)serviceProvider.GetService(enumerableType) ?? Array.Empty<object>();
    }
}
