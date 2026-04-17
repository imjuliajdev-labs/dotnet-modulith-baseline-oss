using BuildingBlocks.Application.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace Architecture.Tests;

public sealed class DispatcherPipelineRegistrationTests
{
    [Fact]
    public void BuiltInDispatcherBehaviorsStayInTheGovernedOrder()
    {
        var services = new ServiceCollection();
        services.AddDispatcher();

        var behaviorRegistrations = services
            .Where(static descriptor =>
                descriptor.ServiceType.IsGenericType &&
                descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IRequestPipelineBehavior<,>))
            .Select(static descriptor => descriptor.ImplementationType?.Name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "RequestContextBehavior`2",
                "RequestExceptionBehavior`2",
                "CancellationBehavior`2",
                "RequestTimeoutBehavior`2",
                "RequestTelemetryBehavior`2",
                "ModuleStateBehavior`2",
                "AuthorizationBehavior`2",
                "CommandIdempotencyBehavior`2",
                "RequestValidationBehavior`2",
                "CommandTransactionBehavior`2"
            },
            behaviorRegistrations);
    }

    [Fact]
    public void DispatcherSubsystemKeepsOneCanonicalRegistrationAndExecutionPath()
    {
        var services = new ServiceCollection();
        services.AddDispatcher();

        Assert.Contains(
            services,
            static descriptor => descriptor.ServiceType == typeof(IDispatcher)
                && string.Equals(descriptor.ImplementationType?.Name, "Dispatcher", StringComparison.Ordinal));
        Assert.Contains(
            services,
            static descriptor => descriptor.ServiceType == typeof(IIntegrationEventDispatcher)
                && string.Equals(descriptor.ImplementationType?.Name, "IntegrationEventDispatcher", StringComparison.Ordinal));

        var registrationSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Dispatching/DispatcherServiceCollectionExtensions.cs");
        var dispatcherSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Dispatching/Dispatcher.cs");
        var integrationEventDispatcherSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Dispatching/IntegrationEventDispatcher.cs");

        Assert.Contains("TryAddScoped<IDispatcher, Dispatcher>()", registrationSource, StringComparison.Ordinal);
        Assert.Contains("TryAddScoped<IIntegrationEventDispatcher, IntegrationEventDispatcher>()", registrationSource, StringComparison.Ordinal);
        Assert.Contains("ValidateDiscovery(discovery)", registrationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MethodInfo.Invoke(", dispatcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MethodInfo.Invoke(", integrationEventDispatcherSource, StringComparison.Ordinal);
    }
}
