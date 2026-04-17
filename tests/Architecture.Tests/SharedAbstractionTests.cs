using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.ProblemDetails;
using BuildingBlocks.Application.ProcessManagers;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Modules;
using BuildingBlocks.Domain.Time;
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Infrastructure.Authorization;
using BuildingBlocks.Infrastructure.Dispatching;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using BuildingBlocks.Infrastructure.Modules;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.ProblemDetails;
using BuildingBlocks.Infrastructure.ProcessManagers;
using BuildingBlocks.Infrastructure.Realtime;
using Microsoft.AspNetCore.Http;
using NodaTime;
using System.Reflection;

namespace Architecture.Tests;

public sealed class SharedAbstractionTests
{
    [Fact]
    public void BuildingBlocksPublicSurfaceMatchesTheApprovedInventory()
    {
        var approvedSurface = RepositoryFiles.ReadApprovedBuildingBlocksPublicSurface();
        var currentSurface = RepositoryFiles.ReadCurrentBuildingBlocksPublicSurface();

        Assert.Equal(
            approvedSurface,
            currentSurface);
    }

    [Fact]
    public void DispatcherContractsExistInBuildingBlocksApplication()
    {
        Assert.Equal("BuildingBlocks.Application", typeof(IDispatcher).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ICommand).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ICommand<>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IQuery<>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IIdempotencyRequest).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IIdempotentCommand).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IIdempotentCommand<>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ICommandHandler<>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ICommandHandler<,>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IQueryHandler<,>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IRequestPipelineBehavior<,>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(RequestContext).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IRequestContextAccessor).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(CurrentActor).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ICurrentActorAccessor).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(RoleRequirement).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IAuthorizeRequest).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IRoleAuthorizer).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IRequireRecentAuthentication).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IRecentAuthenticationAuthorizer).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(RequestTelemetryContext).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IRequestTelemetrySessionFactory).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IRequestTelemetrySession).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(CommandIdempotencyContext).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(CommandIdempotencyAcquireResult).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ICommandIdempotencyExecution).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ICommandIdempotencyStore).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ICommandIdempotencyRequestHasher).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(CommandTransactionContext).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ICommandTransactionScopeFactory).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ICommandTransactionScope).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IRequestValidator<>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IRequestExceptionHandler<,,>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IRequestExceptionAction<,>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IExceptionToErrorMapper).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IIntegrationEventDispatcher).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IIntegrationEventInboxStore).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IIntegrationEventOutboxPublisher).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IIntegrationEventOutboxStore).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IIntegrationEventOutboxDispatcher).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IProcessManagerCheckpointStore).Assembly.GetName().Name);
    }

    [Fact]
    public void ResultAndAuditAbstractionsExistInBuildingBlocksApplication()
    {
        Assert.Equal("BuildingBlocks.Application", typeof(Result).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(Result<>).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(Error).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IAuditEventWriter).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ProblemDetailsContract).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IProblemDetailsMapper).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IModuleStateReader).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IModuleStateGuard).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(IModuleExecutionGate).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Application", typeof(ModuleExecutionLease).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Domain", typeof(ModuleRuntimeState).Assembly.GetName().Name);
    }

    [Fact]
    public void DispatcherEntryPointsAndHandlerContractsStayResultShaped()
    {
        AssertTaskOf(typeof(IDispatcher).GetMethod(nameof(IDispatcher.Send), [typeof(ICommand), typeof(CancellationToken)]), typeof(Result));
        AssertTaskOfOpenGeneric(
            typeof(IDispatcher).GetMethods().Single(method => method.Name == nameof(IDispatcher.Send) && method.IsGenericMethodDefinition),
            typeof(Result<>));
        AssertTaskOfOpenGeneric(
            typeof(IDispatcher).GetMethod(nameof(IDispatcher.Query))!,
            typeof(Result<>));

        AssertTaskOf(typeof(ICommandHandler<ICommand>).GetMethod(nameof(ICommandHandler<ICommand>.Handle))!, typeof(Result));
        AssertTaskOfOpenGeneric(
            typeof(ICommandHandler<ICommand<object?>, object?>).GetMethod(nameof(ICommandHandler<ICommand<object?>, object?>.Handle))!,
            typeof(Result<>));
        AssertTaskOfOpenGeneric(
            typeof(IQueryHandler<IQuery<object?>, object?>).GetMethod(nameof(IQueryHandler<IQuery<object?>, object?>.Handle))!,
            typeof(Result<>));
    }

    [Fact]
    public void DispatcherHotPathAvoidsMethodInfoInvoke()
    {
        var dispatcherSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Dispatching/Dispatcher.cs");
        var integrationEventDispatcherSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Dispatching/IntegrationEventDispatcher.cs");

        Assert.DoesNotContain("handlerMethod.Invoke(", dispatcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("behaviorMethod.Invoke(", dispatcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetInvocationException", dispatcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("handlerMethod.Invoke(", integrationEventDispatcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetInvocationException", integrationEventDispatcherSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DispatcherFailureAndExceptionPathsAvoidReflectiveInvoke()
    {
        var responseFactorySource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Dispatching/DispatcherResponseFactory.cs");
        var exceptionResolverSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Application/Dispatching/RequestExceptionResolver.cs");

        Assert.DoesNotContain(".Invoke(", responseFactorySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Invoke(", exceptionResolverSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresConnectionsAreComposedThroughTheSharedDataSourceResolver()
    {
        var resolverSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Persistence/PostgresDataSourceResolver.cs");
        var advisoryLockSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Persistence/PostgresAdvisoryLock.cs");

        Assert.Contains("interface IPostgresDataSourceResolver", resolverSource, StringComparison.Ordinal);
        Assert.Contains("NpgsqlDataSourceBuilder", resolverSource, StringComparison.Ordinal);
        Assert.Contains("NpgsqlDataSource", advisoryLockSource, StringComparison.Ordinal);

        var violations = RepositoryFiles.ReadCSharpFilesUnder("src")
            .Where(path => RepositoryFiles.ReadAllText(path).Contains("new NpgsqlConnection(", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void DomainEventMarkersExistInBuildingBlocksDomain()
    {
        Assert.Equal("BuildingBlocks.Domain", typeof(IIntegrationEvent).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Domain", typeof(BuildingBlocks.Domain.Time.IClock).Assembly.GetName().Name);
        Assert.Equal("NodaTime", typeof(Instant).Assembly.GetName().Name);
    }

    [Fact]
    public void SharedHttpFailureMappingDoesNotOwnGenericSuccessSerialization()
    {
        var publicMethods = typeof(ResultHttpMapper)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(publicMethods, method => method.Name == "Map");

        var matchMethods = publicMethods.Where(method => method.Name == "Match").ToArray();
        Assert.Equal(2, matchMethods.Length);

        Assert.All(matchMethods, method =>
        {
            var parameters = method.GetParameters();
            Assert.Equal(3, parameters.Length);
            Assert.Equal(typeof(HttpContext), parameters[1].ParameterType);
            Assert.True(typeof(Delegate).IsAssignableFrom(parameters[2].ParameterType));
        });
    }

    [Fact]
    public void TelemetryImplementationLivesInInfrastructure()
    {
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(ActivityRequestTelemetrySessionFactory).Assembly.GetName().Name);
    }

    [Fact]
    public void ClockImplementationLivesInInfrastructure()
    {
        var source = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Time/SystemClockAdapter.cs");

        Assert.Contains("namespace BuildingBlocks.Infrastructure.Time;", source, StringComparison.Ordinal);
        Assert.Contains("SystemClock.Instance.GetCurrentInstant()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentActorHttpAdapterLivesInInfrastructure()
    {
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(HttpContextCurrentActorAccessor).Assembly.GetName().Name);
    }

    [Fact]
    public void ApiModuleCompositionLivesInInfrastructure()
    {
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(IApiModule).Assembly.GetName().Name);
    }

    [Fact]
    public void BrowserRealtimeInfrastructureLivesInInfrastructure()
    {
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(BrowserRealtimeHub).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(IBrowserRealtimeNotifier).Assembly.GetName().Name);
    }

    [Fact]
    public void ModuleEndpointBulkheadInfrastructureLivesInInfrastructure()
    {
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(ModuleEndpointBulkheadPolicyNames).Assembly.GetName().Name);
    }

    [Fact]
    public void DurableEventAndProcessManagerInfrastructureLivesInInfrastructure()
    {
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(PostgresIntegrationEventInboxStore).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(IntegrationEventOutboxPublisher).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(IntegrationEventOutboxDispatcher).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(PostgresModuleIntegrationEventOutboxStore).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(PostgresProcessManagerCheckpointStore).Assembly.GetName().Name);
        Assert.Equal("BuildingBlocks.Infrastructure", typeof(SharedRuntimePersistenceDatabaseMigration).Assembly.GetName().Name);

        var dataProtectionRepositorySource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Persistence/PostgresDataProtectionKeyRepository.cs");
        Assert.Contains("namespace BuildingBlocks.Infrastructure.Persistence;", dataProtectionRepositorySource, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildingBlocksInfrastructureDefaultsDoNotSilentlyRegisterInMemoryDurabilityStores()
    {
        var source = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/ServiceCollectionExtensions.cs");

        Assert.Contains("SharedRuntimePersistenceConfigurationValidationHostedService", source, StringComparison.Ordinal);
        Assert.Contains("IPostgresDataSourceResolver", source, StringComparison.Ordinal);
        Assert.Contains("AddDataProtection()", source, StringComparison.Ordinal);
        Assert.Contains("SetApplicationName(SharedRuntimePersistenceDefaults.DataProtectionApplicationName)", source, StringComparison.Ordinal);
        Assert.Contains("PostgresDataProtectionKeyRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new InMemoryCommandIdempotencyStore()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new InMemoryIntegrationEventInboxStore()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new InMemoryProcessManagerCheckpointStore()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoSharedRuntimeInboxOrCheckpointLiteralsRemainAnywhereInProductionSource()
    {
        // Per ADR-MODULE-OWNED-INBOX-AND-CHECKPOINTS, integration event inbox records and process manager
        // checkpoints live in the owning module's schema, never in starter_runtime. This guard catches any
        // reintroduction of the old shared-runtime table references before they reach main.
        var forbiddenFragments = new[]
        {
            "starter_runtime.building_blocks_integration_event_inbox",
            "starter_runtime.building_blocks_process_manager_checkpoints",
            "building_blocks_integration_event_inbox",
            "building_blocks_process_manager_checkpoints",
        };

        var offenders = new List<string>();
        foreach (var relativePath in RepositoryFiles.ReadCSharpFilesUnder("src"))
        {
            var source = RepositoryFiles.ReadAllText(relativePath);
            foreach (var fragment in forbiddenFragments)
            {
                if (source.Contains(fragment, StringComparison.Ordinal))
                {
                    offenders.Add($"{relativePath}: contains forbidden literal '{fragment}'");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void ApiHostComposesDispatcherAndRequestContextMiddleware()
    {
        var program = RepositoryFiles.ReadAllText("src/ApiHost/Program.cs");
        var composition = RepositoryFiles.ReadAllText("src/ApiHost/ApiHostComposition.cs");

        Assert.Contains("AddBaselineApiHostServices(", program, StringComparison.Ordinal);
        Assert.Contains("MapBaselineApiHost(", program, StringComparison.Ordinal);
        Assert.Contains("AddDispatcher(", composition, StringComparison.Ordinal);
        Assert.Contains("AddHealthChecks(", composition, StringComparison.Ordinal);
        Assert.Contains("AddModuleEndpointBulkheads(", composition, StringComparison.Ordinal);
        Assert.Contains("AddRateLimiter(", composition, StringComparison.Ordinal);
        Assert.Contains("AddBuildingBlocksInfrastructureDefaults(", composition, StringComparison.Ordinal);
        Assert.Contains("AddApiModulesFromAssemblyReferences(", composition, StringComparison.Ordinal);
        Assert.Contains("UseRequestContext(", composition, StringComparison.Ordinal);
        Assert.Contains("UseExceptionHandler(", composition, StringComparison.Ordinal);
        Assert.Contains("UseAuthentication(", composition, StringComparison.Ordinal);
        Assert.Contains("UseAuthorization(", composition, StringComparison.Ordinal);
        Assert.Contains("UseRateLimiter(", composition, StringComparison.Ordinal);
        Assert.Contains("MapHealthChecks(", composition, StringComparison.Ordinal);
        Assert.Contains("MapHub<BrowserRealtimeHub>", composition, StringComparison.Ordinal);
        Assert.Contains("MapApiModules(", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiModuleCompositionAppliesModuleEndpointBulkheads()
    {
        var composition = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Modules/ApiModuleComposition.cs");

        Assert.Contains("RequireRateLimiting(ModuleEndpointBulkheadPolicyNames.For(module.Key))", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiModuleCompositionUsesDescriptorBasedCompositionOrder()
    {
        var composition = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Modules/ApiModuleComposition.cs");

        Assert.Contains("module.Descriptor.CompositionOrder", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("module.Key, \"platform\"", composition, StringComparison.Ordinal);
    }

    private static void AssertTaskOf(MethodInfo? method, Type expectedResultType)
    {
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<>), method!.ReturnType.GetGenericTypeDefinition());
        Assert.Equal(expectedResultType, method.ReturnType.GetGenericArguments()[0]);
    }

    private static void AssertTaskOfOpenGeneric(MethodInfo method, Type expectedOpenGenericType)
    {
        Assert.Equal(typeof(Task<>), method.ReturnType.GetGenericTypeDefinition());

        var taskArgument = method.ReturnType.GetGenericArguments()[0];
        Assert.True(taskArgument.IsGenericType, $"Expected {method.DeclaringType?.FullName}.{method.Name} to return Task<{expectedOpenGenericType.Name}>.");
        Assert.Equal(expectedOpenGenericType, taskArgument.GetGenericTypeDefinition());
    }

    [Fact]
    public void ApiModuleCompositionDiscoversModulesThroughReferencedAssembliesOnly()
    {
        var composition = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Modules/ApiModuleComposition.cs");

        Assert.Contains("GetReferencedAssemblies()", composition, StringComparison.Ordinal);
        Assert.Contains("DependencyContext.Load", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.GetFiles(", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadFromAssemblyPath", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleEntryPointsExposeConcreteDescriptors()
    {
        var modules = RepositoryFiles.ReadModuleApiAssemblies()
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(type => typeof(IApiModule).IsAssignableFrom(type))
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Select(type => (IModule)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Could not create module '{type.FullName}'.")))
            .OrderBy(static module => module.Key, StringComparer.Ordinal)
            .ToArray();

        foreach (var module in modules)
        {
            Assert.False(string.IsNullOrWhiteSpace(module.Key));
            Assert.Equal(module.Key, module.Descriptor.Key);
            Assert.False(string.IsNullOrWhiteSpace(module.Descriptor.DisplayName));
            Assert.StartsWith("/", module.Descriptor.RoutePrefix, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(module.Descriptor.SchemaName));
            Assert.False(string.IsNullOrWhiteSpace(module.Descriptor.ModuleNamespace));
        }
    }
}
