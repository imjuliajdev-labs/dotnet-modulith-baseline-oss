using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using BuildingBlocks.Domain.Time;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Events;

namespace BuildingBlocks.Application.Dispatching;

internal sealed class IntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private static readonly ConcurrentDictionary<(Type ImplementationType, Type EventType), IntegrationEventHandlerInvoker> HandlerInvokerCache = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<string, IIntegrationEventInboxStore> _inboxStores;
    private readonly IModuleExecutionGate _moduleExecutionGate;
    private readonly IExceptionToErrorMapper _exceptionToErrorMapper;
    private readonly IClock _clock;

    private delegate Task IntegrationEventHandlerInvoker(object handler, IIntegrationEvent integrationEvent, CancellationToken cancellationToken);

    public IntegrationEventDispatcher(
        IServiceProvider serviceProvider,
        IEnumerable<IIntegrationEventInboxStore> inboxStores,
        IModuleExecutionGate moduleExecutionGate,
        IExceptionToErrorMapper exceptionToErrorMapper,
        IClock clock)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        ArgumentNullException.ThrowIfNull(inboxStores);
        _inboxStores = inboxStores.ToDictionary(static store => store.ModuleKey, StringComparer.Ordinal);
        _moduleExecutionGate = moduleExecutionGate ?? throw new ArgumentNullException(nameof(moduleExecutionGate));
        _exceptionToErrorMapper = exceptionToErrorMapper ?? throw new ArgumentNullException(nameof(exceptionToErrorMapper));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    private IIntegrationEventInboxStore ResolveInboxStore(string moduleKey)
    {
        if (_inboxStores.TryGetValue(moduleKey, out var store))
        {
            return store;
        }

        throw new InvalidOperationException(
            $"No integration event inbox store is registered for module '{moduleKey}'. Register one via services.AddPostgresIntegrationEventInbox(\"{moduleKey}\", schemaName) in the module's infrastructure extension.");
    }

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        cancellationToken.ThrowIfCancellationRequested();

        var eventType = integrationEvent.GetType();
        var handlerServiceType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var handlers = ResolveServices(handlerServiceType).ToArray();
        if (handlers.Length == 0)
        {
            return;
        }

        List<Exception>? failures = null;

        foreach (var handler in handlers)
        {
            if (handler is not IModuleScoped moduleScoped)
            {
                throw new InvalidOperationException(
                    $"Integration event handler {handler.GetType().FullName} must implement IModuleScoped so deliveries can be isolated by module.");
            }

            var context = new IntegrationEventDeliveryContext(
                moduleScoped.ModuleKey,
                InboxConsumerName.FromHandlerType(handler.GetType()),
                integrationEvent.EventId,
                IntegrationEventTypeNames.GetName(eventType),
                integrationEvent.OccurredAt);

            var inboxStore = ResolveInboxStore(moduleScoped.ModuleKey);

            var beginDecision = await inboxStore.BeginAsync(context, _clock.GetCurrentInstant(), cancellationToken);
            if (beginDecision.Disposition != IntegrationEventDeliveryDisposition.Process)
            {
                continue;
            }

            try
            {
                await using var execution = await _moduleExecutionGate.TryEnterAsync(moduleScoped.ModuleKey, cancellationToken);
                if (!execution.IsEntered)
                {
                    throw new InvalidOperationException(execution.Failure.Message);
                }

                var handlerInvoker = HandlerInvokerCache.GetOrAdd(
                    (handler.GetType(), eventType),
                    static key => CreateHandlerInvoker(key.ImplementationType, key.EventType));

                await handlerInvoker(handler, integrationEvent, cancellationToken);
                await inboxStore.MarkSucceededAsync(context, _clock.GetCurrentInstant(), cancellationToken);
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);

                var mappedError = _exceptionToErrorMapper.Map(exception);
                await inboxStore.MarkFailedAsync(context, _clock.GetCurrentInstant(), mappedError, cancellationToken);
            }
        }

        if (failures is null)
        {
            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException("One or more integration event handlers failed.", failures);
    }

    private IEnumerable<object> ResolveServices(Type serviceType)
    {
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
        return (IEnumerable<object>?)_serviceProvider.GetService(enumerableType) ?? Array.Empty<object>();
    }

    private static IntegrationEventHandlerInvoker CreateHandlerInvoker(Type implementationType, Type eventType)
    {
        var handlerMethod = implementationType.GetMethod(
                                nameof(IIntegrationEventHandler<IIntegrationEvent>.Handle),
                                BindingFlags.Instance | BindingFlags.Public,
                                binder: null,
                                types: new[] { eventType, typeof(CancellationToken) },
                                modifiers: null)
                            ?? throw new InvalidOperationException(
                                $"Integration event handler {implementationType.FullName} does not expose a compatible Handle method.");

        var handlerParameter = Expression.Parameter(typeof(object), "handler");
        var integrationEventParameter = Expression.Parameter(typeof(IIntegrationEvent), "integrationEvent");
        var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var call = Expression.Call(
            Expression.Convert(handlerParameter, implementationType),
            handlerMethod,
            Expression.Convert(integrationEventParameter, eventType),
            cancellationTokenParameter);

        if (call.Type != typeof(Task))
        {
            throw new InvalidOperationException($"Integration event handler {implementationType.FullName} returned an unexpected result.");
        }

        return Expression.Lambda<IntegrationEventHandlerInvoker>(
            call,
            handlerParameter,
            integrationEventParameter,
            cancellationTokenParameter).Compile();
    }
}
