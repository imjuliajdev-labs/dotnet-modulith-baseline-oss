using System.Reflection;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching.Behaviors;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Domain.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Application.Dispatching;

public static class DispatcherServiceCollectionExtensions
{
    public static IServiceCollection AddDispatcher(
        this IServiceCollection services,
        params Assembly[] requestAssemblies)
    {
        return AddDispatcher(services, configure: null, requestAssemblies);
    }

    public static IServiceCollection AddDispatcher(
        this IServiceCollection services,
        Action<DispatcherOptions>? configure,
        params Assembly[] requestAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DispatcherOptions();
        configure?.Invoke(options);

        services.TryAddScoped<IDispatcher, Dispatcher>();
        services.TryAddScoped<IIntegrationEventDispatcher, IntegrationEventDispatcher>();
        services.TryAddSingleton<IRequestContextAccessor, AsyncLocalRequestContextAccessor>();
        services.TryAddScoped<ICurrentActorAccessor, AnonymousCurrentActorAccessor>();
        services.TryAddSingleton<IRoleAuthorizer, CurrentActorRoleAuthorizer>();
        services.TryAddSingleton<IModuleWorkLeaseManager, NoOpModuleWorkLeaseManager>();
        services.TryAddSingleton<IModuleExecutionGate, ModuleExecutionGate>();
        services.TryAddSingleton<IRecentAuthenticationAuthorizer>(static provider =>
            new CurrentActorRecentAuthenticationAuthorizer(provider.GetService<IClock>()));
        services.TryAddSingleton<IRequestTelemetrySessionFactory, NoOpRequestTelemetrySessionFactory>();
        services.TryAddScoped<ICommandTransactionScopeFactory, NoOpCommandTransactionScopeFactory>();

        if (options.RegisterBuiltInBehaviors)
        {
            RegisterBuiltInBehaviors(services);
        }

        foreach (var behaviorType in options.AdditionalPipelineBehaviorTypes.Distinct())
        {
            ValidateBehaviorType(behaviorType);
            services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), behaviorType));
        }

        var assemblies = requestAssemblies
            .Where(static assembly => assembly is not null)
            .Distinct()
            .ToArray();

        if (assemblies.Length == 0)
        {
            return services;
        }

        var discovery = Discover(assemblies);

        if (options.EnableStartupValidation)
        {
            ValidateDiscovery(discovery);
        }

        RegisterDiscoveredServices(services, discovery);
        return services;
    }

    private static void RegisterBuiltInBehaviors(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), typeof(RequestContextBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), typeof(RequestExceptionBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), typeof(CancellationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), typeof(RequestTimeoutBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), typeof(RequestTelemetryBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), typeof(ModuleStateBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), typeof(AuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), typeof(CommandIdempotencyBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), typeof(RequestValidationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestPipelineBehavior<,>), typeof(CommandTransactionBehavior<,>)));
    }

    private static void ValidateBehaviorType(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        var implementsBehavior = behaviorType
            .GetInterfaces()
            .Any(static interfaceType =>
                interfaceType.IsGenericType &&
                interfaceType.GetGenericTypeDefinition() == typeof(IRequestPipelineBehavior<,>));

        if (!implementsBehavior)
        {
            throw new InvalidOperationException(
                $"Pipeline behavior type {behaviorType.FullName} must implement IRequestPipelineBehavior<TRequest, TResponse>.");
        }
    }

    private static DispatcherDiscovery Discover(IEnumerable<Assembly> assemblies)
    {
        var requests = new List<RequestDescriptor>();
        var handlers = new List<ServiceRegistration>();
        var integrationEventHandlers = new List<ServiceRegistration>();
        var validators = new List<ServiceRegistration>();
        var exceptionHandlers = new List<ServiceRegistration>();
        var exceptionActions = new List<ServiceRegistration>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (!IsConcrete(type))
                {
                    continue;
                }

                foreach (var implementedInterface in type.GetInterfaces())
                {
                    if (implementedInterface == typeof(ICommand))
                    {
                        requests.Add(new RequestDescriptor(
                            type,
                            typeof(ICommandHandler<>).MakeGenericType(type),
                            ContractKind.Command));

                        continue;
                    }

                    if (!implementedInterface.IsGenericType)
                    {
                        continue;
                    }

                    var genericTypeDefinition = implementedInterface.GetGenericTypeDefinition();
                    var genericArguments = implementedInterface.GetGenericArguments();

                    if (genericTypeDefinition == typeof(ICommand<>))
                    {
                        requests.Add(new RequestDescriptor(
                            type,
                            typeof(ICommandHandler<,>).MakeGenericType(type, genericArguments[0]),
                            ContractKind.Command));

                        continue;
                    }

                    if (genericTypeDefinition == typeof(IQuery<>))
                    {
                        requests.Add(new RequestDescriptor(
                            type,
                            typeof(IQueryHandler<,>).MakeGenericType(type, genericArguments[0]),
                            ContractKind.Query));

                        continue;
                    }

                    if (genericTypeDefinition == typeof(ICommandHandler<>) ||
                        genericTypeDefinition == typeof(ICommandHandler<,>) ||
                        genericTypeDefinition == typeof(IQueryHandler<,>))
                    {
                        handlers.Add(new ServiceRegistration(implementedInterface, type));
                        continue;
                    }

                    if (genericTypeDefinition == typeof(IIntegrationEventHandler<>))
                    {
                        integrationEventHandlers.Add(new ServiceRegistration(implementedInterface, type));
                        continue;
                    }

                    if (genericTypeDefinition == typeof(IRequestValidator<>))
                    {
                        validators.Add(new ServiceRegistration(implementedInterface, type));
                        continue;
                    }

                    if (genericTypeDefinition == typeof(IRequestExceptionHandler<,,>))
                    {
                        exceptionHandlers.Add(new ServiceRegistration(implementedInterface, type));
                        continue;
                    }

                    if (genericTypeDefinition == typeof(IRequestExceptionAction<,>))
                    {
                        exceptionActions.Add(new ServiceRegistration(implementedInterface, type));
                    }
                }
            }
        }

        return new DispatcherDiscovery(
            requests
                .DistinctBy(static descriptor => (descriptor.RequestType, descriptor.HandlerServiceType))
                .ToArray(),
            handlers
                .DistinctBy(static descriptor => (descriptor.ServiceType, descriptor.ImplementationType))
                .ToArray(),
            integrationEventHandlers
                .DistinctBy(static descriptor => (descriptor.ServiceType, descriptor.ImplementationType))
                .ToArray(),
            validators
                .DistinctBy(static descriptor => (descriptor.ServiceType, descriptor.ImplementationType))
                .ToArray(),
            exceptionHandlers
                .DistinctBy(static descriptor => (descriptor.ServiceType, descriptor.ImplementationType))
                .ToArray(),
            exceptionActions
                .DistinctBy(static descriptor => (descriptor.ServiceType, descriptor.ImplementationType))
                .ToArray());
    }

    private static void ValidateDiscovery(DispatcherDiscovery discovery)
    {
        foreach (var duplicateRequestKind in discovery.Requests.GroupBy(static descriptor => descriptor.RequestType))
        {
            var serviceTypes = duplicateRequestKind
                .Select(static descriptor => descriptor.HandlerServiceType)
                .Distinct()
                .ToArray();

            if (serviceTypes.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Request {duplicateRequestKind.Key.FullName} implements multiple dispatcher request contracts. Requests must be commands or queries, not both.");
            }
        }

        foreach (var duplicateHandler in discovery.Handlers.GroupBy(static descriptor => descriptor.ServiceType).Where(static group => group.Count() > 1))
        {
            var implementations = string.Join(", ", duplicateHandler.Select(static descriptor => descriptor.ImplementationType.FullName));

            throw new InvalidOperationException(
                $"Duplicate dispatcher handlers found for {duplicateHandler.Key.FullName}: {implementations}");
        }

        foreach (var duplicateExceptionHandler in discovery.ExceptionHandlers.GroupBy(static descriptor => descriptor.ServiceType).Where(static group => group.Count() > 1))
        {
            var implementations = string.Join(", ", duplicateExceptionHandler.Select(static descriptor => descriptor.ImplementationType.FullName));

            throw new InvalidOperationException(
                $"Duplicate request exception handlers found for {duplicateExceptionHandler.Key.FullName}: {implementations}");
        }

        var missingHandlers = discovery.Requests
            .Where(request => discovery.Handlers.All(handler => handler.ServiceType != request.HandlerServiceType))
            .ToArray();

        if (missingHandlers.Length > 0)
        {
            var missing = string.Join(", ", missingHandlers.Select(static descriptor => descriptor.RequestType.FullName));
            throw new InvalidOperationException($"Dispatcher startup validation failed. Missing handlers for: {missing}");
        }
    }

    private static void RegisterDiscoveredServices(IServiceCollection services, DispatcherDiscovery discovery)
    {
        foreach (var handler in discovery.Handlers)
        {
            services.AddScoped(handler.ServiceType, handler.ImplementationType);
        }

        foreach (var integrationEventHandler in discovery.IntegrationEventHandlers)
        {
            services.AddScoped(integrationEventHandler.ServiceType, integrationEventHandler.ImplementationType);
        }

        foreach (var validator in discovery.Validators)
        {
            services.AddTransient(validator.ServiceType, validator.ImplementationType);
        }

        foreach (var exceptionHandler in discovery.ExceptionHandlers)
        {
            services.AddTransient(exceptionHandler.ServiceType, exceptionHandler.ImplementationType);
        }

        foreach (var exceptionAction in discovery.ExceptionActions)
        {
            services.AddTransient(exceptionAction.ServiceType, exceptionAction.ImplementationType);
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(static type => type is not null)!;
        }
    }

    private static bool IsConcrete(Type type)
    {
        return !type.IsInterface &&
               !type.IsAbstract &&
               !type.IsGenericTypeDefinition &&
               !type.ContainsGenericParameters;
    }

    private enum ContractKind
    {
        Command,
        Query
    }

    private sealed record RequestDescriptor(Type RequestType, Type HandlerServiceType, ContractKind Kind);

    private sealed record ServiceRegistration(Type ServiceType, Type ImplementationType);

    private sealed record DispatcherDiscovery(
        IReadOnlyCollection<RequestDescriptor> Requests,
        IReadOnlyCollection<ServiceRegistration> Handlers,
        IReadOnlyCollection<ServiceRegistration> IntegrationEventHandlers,
        IReadOnlyCollection<ServiceRegistration> Validators,
        IReadOnlyCollection<ServiceRegistration> ExceptionHandlers,
        IReadOnlyCollection<ServiceRegistration> ExceptionActions);
}
