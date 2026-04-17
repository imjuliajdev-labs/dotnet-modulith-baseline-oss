using BuildingBlocks.Application.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BuildingBlocks.Infrastructure.Modules;

public interface IApiModule : IModule
{
    void AddServices(IServiceCollection services);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

public static class ApiModuleCompositionExtensions
{
    public static IServiceCollection AddApiModulesFromAssemblyReferences(this IServiceCollection services, Assembly rootAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rootAssembly);

        var rootAssemblyName = rootAssembly.GetName().Name;
        var referencedModuleAssemblies = rootAssembly
            .GetReferencedAssemblies()
            .Where(static assemblyName => assemblyName.Name?.EndsWith(".Api", StringComparison.Ordinal) == true)
            .Where(assemblyName => !string.Equals(assemblyName.Name, rootAssemblyName, StringComparison.Ordinal))
            .Select(Assembly.Load)
            .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();

        var dependencyContextModuleAssemblies = (DependencyContext.Load(rootAssembly) ?? DependencyContext.Default)
            ?.RuntimeLibraries
            .Where(static library => library.Name.EndsWith(".Api", StringComparison.Ordinal))
            .Where(library => !string.Equals(library.Name, rootAssemblyName, StringComparison.Ordinal))
            .Select(static library => Assembly.Load(new AssemblyName(library.Name)))
            .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<Assembly>();

        var moduleAssemblies = referencedModuleAssemblies
            .Concat(dependencyContextModuleAssemblies)
            .GroupBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();

        return services.AddApiModulesFromAssemblies(moduleAssemblies);
    }

    public static IServiceCollection AddApiModulesFromAssemblies(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        var modules = assemblies
            .Where(static assembly => assembly is not null)
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(type => typeof(IApiModule).IsAssignableFrom(type))
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Distinct()
            .Select(CreateApiModule)
            .GroupBy(static module => module.GetType().FullName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static module => module.Descriptor.CompositionOrder)
            .ThenBy(static module => module.Key, StringComparer.Ordinal)
            .ToArray();

        foreach (var module in modules)
        {
            AddApiModule(services, module);
        }

        return services;
    }

    public static IServiceCollection AddApiModule<TModule>(this IServiceCollection services)
        where TModule : class, IApiModule, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        var module = new TModule();
        AddApiModule(services, module);

        return services;
    }

    private static IApiModule CreateApiModule(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);

        if (!typeof(IApiModule).IsAssignableFrom(moduleType) || moduleType is not { IsClass: true, IsAbstract: false })
        {
            throw new ArgumentException($"Type '{moduleType.FullName}' is not a concrete {nameof(IApiModule)}.", nameof(moduleType));
        }

        return (IApiModule)(Activator.CreateInstance(moduleType)
            ?? throw new InvalidOperationException($"Could not create API module '{moduleType.FullName}'."));
    }

    private static void AddApiModule(IServiceCollection services, IApiModule module)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(module);

        module.AddServices(services);
        services.AddSingleton<IModule>(module);
        services.AddSingleton<IApiModule>(module);

        if (module is IModuleRateLimiterContributor rateLimiterContributor)
        {
            rateLimiterContributor.RegisterRateLimiterPolicies(services);
        }
    }

    public static IEndpointRouteBuilder MapApiModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var versionedGroup = endpoints.MapGroup("/api/v1");

        foreach (var module in endpoints.ServiceProvider.GetServices<IApiModule>())
        {
            var moduleGroup = versionedGroup.MapGroup(module.Descriptor.RoutePrefix)
                .WithTags(module.Descriptor.DisplayName)
                .RequireRateLimiting(ModuleEndpointBulkheadPolicyNames.For(module.Key));

            if (module.Descriptor.CanBeDisabled)
            {
                moduleGroup.AddEndpointFilter(new ModuleStateEndpointFilter(module.Key));
                moduleGroup.ProducesProblem(StatusCodes.Status503ServiceUnavailable);
            }

            module.MapEndpoints(moduleGroup);
        }

        return endpoints;
    }
}
