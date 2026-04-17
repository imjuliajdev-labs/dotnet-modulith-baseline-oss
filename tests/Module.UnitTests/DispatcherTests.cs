using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Module.UnitTests;

public sealed class DispatcherTests
{
    [Fact]
    public async Task AddDispatcherCanDiscoverHandlersFromAssemblies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher(typeof(DispatcherTests).Assembly);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var commandResult = await dispatcher.Send(new DiscoveredCommand("governed"));
        var queryResult = await dispatcher.Query(new DiscoveredQuery(21));

        Assert.True(commandResult.IsSuccess);
        Assert.Equal("governed-handled", commandResult.Value);

        Assert.True(queryResult.IsSuccess);
        Assert.Equal(42, queryResult.Value);
    }

    [Fact]
    public async Task DispatcherRunsValidationBeforeInvokingTheHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton(new TransactionTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<ValidatedCommand, string>, ValidatedCommandHandler>();
        services.AddTransient<IRequestValidator<ValidatedCommand>, ValidatedCommandValidator>();
        services.AddSingleton<ICommandTransactionScopeFactory, TrackingCommandTransactionScopeFactory>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var tracker = scope.ServiceProvider.GetRequiredService<InvocationTracker>();
        var transactionTracker = scope.ServiceProvider.GetRequiredService<TransactionTracker>();

        var result = await dispatcher.Send(new ValidatedCommand(string.Empty));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal(0, tracker.ValidatedCommandInvocations);
        Assert.Equal(0, transactionTracker.BeginCount);
    }

    [Fact]
    public async Task DispatcherCreatesAmbientRequestContextWhenMissing()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RequestContextTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<RequestContextCommand, string>, RequestContextCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var requestContextAccessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();
        var tracker = scope.ServiceProvider.GetRequiredService<RequestContextTracker>();

        var result = await dispatcher.Send(new RequestContextCommand("ambient"));

        Assert.True(result.IsSuccess);
        Assert.Equal("ambient", result.Value);
        Assert.NotNull(tracker.LastSeenContext);
        Assert.False(string.IsNullOrWhiteSpace(tracker.LastSeenContext!.CorrelationId));
        Assert.False(string.IsNullOrWhiteSpace(tracker.LastSeenContext.RequestId));
        Assert.Null(requestContextAccessor.Current);
    }

    [Fact]
    public async Task DispatcherReusesAmbientRequestContextWhenAlreadyPresent()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RequestContextTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<RequestContextCommand, string>, RequestContextCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var requestContextAccessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();
        var tracker = scope.ServiceProvider.GetRequiredService<RequestContextTracker>();

        requestContextAccessor.Current = new RequestContext("corr-123", "req-123");

        var result = await dispatcher.Send(new RequestContextCommand("ambient"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(tracker.LastSeenContext);
        Assert.Equal("corr-123", tracker.LastSeenContext!.CorrelationId);
        Assert.Equal("req-123", tracker.LastSeenContext.RequestId);
        Assert.Equal("corr-123", requestContextAccessor.Current?.CorrelationId);
        Assert.Equal("req-123", requestContextAccessor.Current?.RequestId);
    }

    [Fact]
    public async Task DispatcherCompletesTelemetryAfterSuccessfulRequests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TelemetryTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddSingleton<IRequestTelemetrySessionFactory, TrackingRequestTelemetrySessionFactory>();
        services.AddScoped<ICommandHandler<TelemetryCommand, string>, TelemetryCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var requestContextAccessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();
        var tracker = scope.ServiceProvider.GetRequiredService<TelemetryTracker>();

        requestContextAccessor.Current = new RequestContext("corr-123", "req-123");

        var result = await dispatcher.Send(new TelemetryCommand("admin", "ok"));

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
        Assert.Equal(new[] { "telemetry-start", "handler", "telemetry-success" }, tracker.Events);
        Assert.Single(tracker.Contexts);
        Assert.Equal(typeof(TelemetryCommand), tracker.Contexts[0].RequestType);
        Assert.Equal("command", tracker.Contexts[0].RequestKind);
        Assert.Equal("corr-123", tracker.Contexts[0].CorrelationId);
        Assert.Equal("req-123", tracker.Contexts[0].RequestId);
        Assert.Equal("admin", tracker.Contexts[0].ModuleKey);
    }

    [Fact]
    public async Task DispatcherPassesCommandsThroughTheTransactionBoundaryButSkipsQueries()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TransactionTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddSingleton<ICommandTransactionScopeFactory, TrackingCommandTransactionScopeFactory>();
        services.AddScoped<ICommandHandler<TransactionCommand, string>, TransactionCommandHandler>();
        services.AddScoped<IQueryHandler<TransactionQuery, int>, TransactionQueryHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var transactionTracker = scope.ServiceProvider.GetRequiredService<TransactionTracker>();

        var commandResult = await dispatcher.Send(new TransactionCommand("admin", "committed"));
        var queryResult = await dispatcher.Query(new TransactionQuery(21));

        Assert.True(commandResult.IsSuccess);
        Assert.Equal("committed", commandResult.Value);
        Assert.True(queryResult.IsSuccess);
        Assert.Equal(42, queryResult.Value);
        Assert.Equal(1, transactionTracker.BeginCount);
        Assert.Equal(1, transactionTracker.CommitCount);
        Assert.Equal(0, transactionTracker.RollbackCount);
        Assert.Single(transactionTracker.Contexts);
        Assert.Equal(typeof(TransactionCommand), transactionTracker.Contexts[0].CommandType);
        Assert.Equal("admin", transactionTracker.Contexts[0].ModuleKey);
    }

    [Fact]
    public async Task DispatcherReplaysCompletedOutcomesForRepeatedIdempotentCommands()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton(new TestCommandIdempotencyStore());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddSingleton<ICommandIdempotencyStore>(static provider => provider.GetRequiredService<TestCommandIdempotencyStore>());
        services.AddSingleton<ICommandIdempotencyRequestHasher, TestCommandIdempotencyRequestHasher>();
        services.AddScoped<ICommandHandler<ReplayableIdempotentCommand, string>, ReplayableIdempotentCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var tracker = scope.ServiceProvider.GetRequiredService<InvocationTracker>();

        var first = await dispatcher.Send(new ReplayableIdempotentCommand("request-1", "alpha"));
        var second = await dispatcher.Send(new ReplayableIdempotentCommand("request-1", "alpha"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal("alpha-handled", first.Value);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(1, tracker.ReplayableIdempotentCommandInvocations);
    }

    [Fact]
    public async Task DispatcherRejectsIdempotentCommandsWhenTheSameKeyHasADifferentPayload()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton(new TestCommandIdempotencyStore());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddSingleton<ICommandIdempotencyStore>(static provider => provider.GetRequiredService<TestCommandIdempotencyStore>());
        services.AddSingleton<ICommandIdempotencyRequestHasher, TestCommandIdempotencyRequestHasher>();
        services.AddScoped<ICommandHandler<ReplayableIdempotentCommand, string>, ReplayableIdempotentCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var tracker = scope.ServiceProvider.GetRequiredService<InvocationTracker>();

        var first = await dispatcher.Send(new ReplayableIdempotentCommand("request-1", "alpha"));
        var second = await dispatcher.Send(new ReplayableIdempotentCommand("request-1", "beta"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal(ErrorKind.Conflict, second.Error.Kind);
        Assert.Equal("idempotency.request_conflict", second.Error.Code);
        Assert.Equal(1, tracker.ReplayableIdempotentCommandInvocations);
    }

    [Fact]
    public async Task DispatcherMapsUnhandledExceptionsThroughTheSharedMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TransactionTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddSingleton<ICommandTransactionScopeFactory, TrackingCommandTransactionScopeFactory>();
        services.AddScoped<ICommandHandler<ThrowingCommand, string>, ThrowingCommandHandler>();
        services.AddSingleton<IExceptionToErrorMapper>(new StubExceptionMapper("mapped.failure", ErrorKind.Failure));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var transactionTracker = scope.ServiceProvider.GetRequiredService<TransactionTracker>();

        var result = await dispatcher.Send(new ThrowingCommand());

        Assert.True(result.IsFailure);
        Assert.Equal("mapped.failure", result.Error.Code);
        Assert.Equal(1, transactionTracker.BeginCount);
        Assert.Equal(0, transactionTracker.CommitCount);
        Assert.Equal(1, transactionTracker.RollbackCount);
    }

    [Fact]
    public async Task DispatcherRollsBackTheTransactionWhenACommandReturnsFailure()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TransactionTracker());
        services.AddSingleton(new TelemetryTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddSingleton<ICommandTransactionScopeFactory, TrackingCommandTransactionScopeFactory>();
        services.AddSingleton<IRequestTelemetrySessionFactory, TrackingRequestTelemetrySessionFactory>();
        services.AddScoped<ICommandHandler<FailingCommand, string>, FailingCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var transactionTracker = scope.ServiceProvider.GetRequiredService<TransactionTracker>();
        var telemetryTracker = scope.ServiceProvider.GetRequiredService<TelemetryTracker>();

        var result = await dispatcher.Send(new FailingCommand());

        Assert.True(result.IsFailure);
        Assert.Equal("command.failed", result.Error.Code);
        Assert.Equal(1, transactionTracker.BeginCount);
        Assert.Equal(0, transactionTracker.CommitCount);
        Assert.Equal(1, transactionTracker.RollbackCount);
        Assert.Equal(new[] { "telemetry-start", "handler", "telemetry-failure" }, telemetryTracker.Events);
        Assert.Equal("command.failed", telemetryTracker.ErrorCode);
    }

    [Fact]
    public async Task DispatcherRecordsTelemetryFailureBeforeRequestExceptionActions()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TelemetryTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddSingleton<IRequestTelemetrySessionFactory, TrackingRequestTelemetrySessionFactory>();
        services.AddScoped<ICommandHandler<TelemetryThrowingCommand, string>, TelemetryThrowingCommandHandler>();
        services.AddTransient<IRequestExceptionAction<TelemetryThrowingCommand, InvalidOperationException>, TelemetryExceptionAction>();
        services.AddSingleton<IExceptionToErrorMapper>(new StubExceptionMapper("telemetry.failure", ErrorKind.Failure));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var requestContextAccessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();
        var tracker = scope.ServiceProvider.GetRequiredService<TelemetryTracker>();

        requestContextAccessor.Current = new RequestContext("corr-123", "req-123");

        var result = await dispatcher.Send(new TelemetryThrowingCommand("admin"));

        Assert.True(result.IsFailure);
        Assert.Equal("telemetry.failure", result.Error.Code);
        Assert.Equal(new[] { "telemetry-start", "handler", "telemetry-exception", "exception-action" }, tracker.Events);
        Assert.Equal(typeof(InvalidOperationException).FullName, tracker.ExceptionType);
    }

    [Fact]
    public async Task DispatcherUsesRequestSpecificExceptionHandlersBeforeTheSharedMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<HandledExceptionCommand, string>, HandledExceptionCommandHandler>();
        services.AddTransient<IRequestExceptionHandler<HandledExceptionCommand, Result<string>, DomainSpecificException>, HandledExceptionCommandExceptionHandler>();
        services.AddSingleton<IExceptionToErrorMapper>(new StubExceptionMapper("fallback.mapper", ErrorKind.Failure));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(new HandledExceptionCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal("handled-specific-exception", result.Value);
    }

    [Fact]
    public async Task DispatcherExecutesRequestExceptionActionsBeforeReturningFailure()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ExceptionActionTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<ActionCommand, string>, ActionCommandHandler>();
        services.AddTransient<IRequestExceptionAction<ActionCommand, InvalidOperationException>, ActionCommandExceptionAction>();
        services.AddSingleton<IExceptionToErrorMapper>(new StubExceptionMapper("action.failure", ErrorKind.Failure));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var tracker = scope.ServiceProvider.GetRequiredService<ExceptionActionTracker>();

        var result = await dispatcher.Send(new ActionCommand());

        Assert.True(result.IsFailure);
        Assert.Equal("action.failure", result.Error.Code);
        Assert.Equal(1, tracker.ExecutionCount);
        Assert.False(string.IsNullOrWhiteSpace(tracker.CorrelationId));
    }

    [Fact]
    public async Task DispatcherShortCircuitsScopedRequestsWhenTheModuleIsNotEnabled()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(
            new Error("module.disabled", "Module 'admin' is disabled.", ErrorKind.ServiceUnavailable)));
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<ModuleScopedCommand, string>, ModuleScopedCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var tracker = scope.ServiceProvider.GetRequiredService<InvocationTracker>();

        var result = await dispatcher.Send(new ModuleScopedCommand("admin", "blocked"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ServiceUnavailable, result.Error.Kind);
        Assert.Equal("module.disabled", result.Error.Code);
        Assert.Equal(0, tracker.ModuleScopedCommandInvocations);
    }

    [Fact]
    public async Task DispatcherAllowsScopedRequestsWhenTheModuleIsEnabled()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<ModuleScopedCommand, string>, ModuleScopedCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var tracker = scope.ServiceProvider.GetRequiredService<InvocationTracker>();

        var result = await dispatcher.Send(new ModuleScopedCommand("admin", "allowed"));

        Assert.True(result.IsSuccess);
        Assert.Equal("allowed", result.Value);
        Assert.Equal(1, tracker.ModuleScopedCommandInvocations);
    }

    [Fact]
    public async Task DispatcherReturnsUnauthorizedForOptedInRequestsWhenTheCurrentActorIsAnonymous()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton(new TelemetryTracker());
        services.AddSingleton(new TransactionTracker());
        services.AddSingleton(new TestCommandIdempotencyStore());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddSingleton<IRequestTelemetrySessionFactory, TrackingRequestTelemetrySessionFactory>();
        services.AddSingleton<ICommandTransactionScopeFactory, TrackingCommandTransactionScopeFactory>();
        services.AddSingleton<ICommandIdempotencyStore>(static provider => provider.GetRequiredService<TestCommandIdempotencyStore>());
        services.AddSingleton<ICommandIdempotencyRequestHasher, TestCommandIdempotencyRequestHasher>();
        services.AddScoped<ICommandHandler<ProtectedCommand, string>, ProtectedCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var tracker = scope.ServiceProvider.GetRequiredService<InvocationTracker>();
        var telemetryTracker = scope.ServiceProvider.GetRequiredService<TelemetryTracker>();
        var transactionTracker = scope.ServiceProvider.GetRequiredService<TransactionTracker>();
        var idempotencyStore = scope.ServiceProvider.GetRequiredService<TestCommandIdempotencyStore>();

        var result = await dispatcher.Send(new ProtectedCommand("admin", "request-1", "blocked"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Unauthorized, result.Error.Kind);
        Assert.Equal("auth.unauthorized", result.Error.Code);
        Assert.Equal(0, tracker.ProtectedCommandInvocations);
        Assert.Equal(0, idempotencyStore.BeginCount);
        Assert.Equal(0, transactionTracker.BeginCount);
        Assert.Equal(new[] { "telemetry-start", "telemetry-failure" }, telemetryTracker.Events);
        Assert.Equal("auth.unauthorized", telemetryTracker.ErrorCode);
    }

    [Fact]
    public async Task DispatcherReturnsForbiddenWhenTheCurrentActorLacksARequiredRole()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton(new TelemetryTracker());
        services.AddSingleton(new TransactionTracker());
        services.AddSingleton(new TestCommandIdempotencyStore());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddSingleton<ICurrentActorAccessor>(new TestCurrentActorAccessor(
            new CurrentActor("user-1", isAuthenticated: true, roles: ["User"])));
        services.AddSingleton<IRequestTelemetrySessionFactory, TrackingRequestTelemetrySessionFactory>();
        services.AddSingleton<ICommandTransactionScopeFactory, TrackingCommandTransactionScopeFactory>();
        services.AddSingleton<ICommandIdempotencyStore>(static provider => provider.GetRequiredService<TestCommandIdempotencyStore>());
        services.AddSingleton<ICommandIdempotencyRequestHasher, TestCommandIdempotencyRequestHasher>();
        services.AddScoped<ICommandHandler<ProtectedCommand, string>, ProtectedCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var tracker = scope.ServiceProvider.GetRequiredService<InvocationTracker>();
        var telemetryTracker = scope.ServiceProvider.GetRequiredService<TelemetryTracker>();
        var transactionTracker = scope.ServiceProvider.GetRequiredService<TransactionTracker>();
        var idempotencyStore = scope.ServiceProvider.GetRequiredService<TestCommandIdempotencyStore>();

        var result = await dispatcher.Send(new ProtectedCommand("admin", "request-1", "blocked"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Forbidden, result.Error.Kind);
        Assert.Equal("auth.forbidden", result.Error.Code);
        Assert.Equal(0, tracker.ProtectedCommandInvocations);
        Assert.Equal(0, idempotencyStore.BeginCount);
        Assert.Equal(0, transactionTracker.BeginCount);
        Assert.Equal(new[] { "telemetry-start", "telemetry-failure" }, telemetryTracker.Events);
        Assert.Equal("auth.forbidden", telemetryTracker.ErrorCode);
    }

    [Fact]
    public async Task DispatcherAllowsOptedInRequestsWhenTheCurrentActorHasTheRequiredRole()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton(new TelemetryTracker());
        services.AddSingleton(new TransactionTracker());
        services.AddSingleton(new TestCommandIdempotencyStore());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddSingleton<ICurrentActorAccessor>(new TestCurrentActorAccessor(
            new CurrentActor("user-1", isAuthenticated: true, roles: ["Admin"])));
        services.AddSingleton<IRequestTelemetrySessionFactory, TrackingRequestTelemetrySessionFactory>();
        services.AddSingleton<ICommandTransactionScopeFactory, TrackingCommandTransactionScopeFactory>();
        services.AddSingleton<ICommandIdempotencyStore>(static provider => provider.GetRequiredService<TestCommandIdempotencyStore>());
        services.AddSingleton<ICommandIdempotencyRequestHasher, TestCommandIdempotencyRequestHasher>();
        services.AddScoped<ICommandHandler<ProtectedCommand, string>, ProtectedCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var tracker = scope.ServiceProvider.GetRequiredService<InvocationTracker>();
        var telemetryTracker = scope.ServiceProvider.GetRequiredService<TelemetryTracker>();
        var transactionTracker = scope.ServiceProvider.GetRequiredService<TransactionTracker>();
        var idempotencyStore = scope.ServiceProvider.GetRequiredService<TestCommandIdempotencyStore>();

        var result = await dispatcher.Send(new ProtectedCommand("admin", "request-1", "allowed"));

        Assert.True(result.IsSuccess);
        Assert.Equal("allowed", result.Value);
        Assert.Equal(1, tracker.ProtectedCommandInvocations);
        Assert.Equal(1, idempotencyStore.BeginCount);
        Assert.Equal(1, transactionTracker.BeginCount);
        Assert.Equal(1, transactionTracker.CommitCount);
        Assert.Equal(0, transactionTracker.RollbackCount);
        Assert.Equal(new[] { "telemetry-start", "handler", "telemetry-success" }, telemetryTracker.Events);
    }

    [Fact]
    public async Task RequestTimeoutBehaviorCancelsHandlerWhenTimeoutElapses()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<TimeoutSpinningCommand, string>, TimeoutSpinningCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(new TimeoutSpinningCommand(TimeSpan.FromMilliseconds(50)));

        Assert.True(result.IsFailure);
        Assert.Equal("request.timeout", result.Error.Code);
        Assert.Equal(ErrorKind.Cancelled, result.Error.Kind);
    }

    [Fact]
    public async Task RequestTimeoutBehaviorPropagatesOuterCancellationWithoutMaskingItAsTimeout()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<TimeoutSpinningCommand, string>, TimeoutSpinningCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dispatcher.Send(new TimeoutSpinningCommand(TimeSpan.FromSeconds(30)), cts.Token));
    }

    [Fact]
    public async Task RequestTimeoutBehaviorDoesNotLeaveOrphanWorkRunningAfterTimeout()
    {
        var sentinel = new OrphanWorkSentinel();
        var services = new ServiceCollection();
        services.AddSingleton(sentinel);
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<OrphanProbeCommand, string>, OrphanProbeCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var timeout = TimeSpan.FromMilliseconds(50);
        var result = await dispatcher.Send(new OrphanProbeCommand(timeout));

        Assert.True(result.IsFailure);
        Assert.Equal("request.timeout", result.Error.Code);

        // Wait well past the handler's intended write time. If the handler had been allowed to run to completion
        // (no cancellation propagation), the sentinel would be set by now.
        await Task.Delay(TimeSpan.FromMilliseconds(timeout.TotalMilliseconds * 4));
        Assert.False(sentinel.WasWrittenAfterTimeout);
    }

    [Fact]
    public async Task DispatcherChecksModuleStateBeforeAuthorizationForProtectedRequests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new InvocationTracker());
        services.AddSingleton(new RoleAuthorizerTracker());
        services.AddSingleton(new TestCommandIdempotencyStore());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(
            new Error("module.disabled", "Module 'admin' is disabled.", ErrorKind.ServiceUnavailable)));
        services.AddDispatcher();
        services.AddSingleton<ICurrentActorAccessor>(new TestCurrentActorAccessor(
            new CurrentActor("user-1", isAuthenticated: true, roles: ["Admin"])));
        services.AddSingleton<IRoleAuthorizer, TrackingRoleAuthorizer>();
        services.AddSingleton<ICommandIdempotencyStore>(static provider => provider.GetRequiredService<TestCommandIdempotencyStore>());
        services.AddSingleton<ICommandIdempotencyRequestHasher, TestCommandIdempotencyRequestHasher>();
        services.AddScoped<ICommandHandler<ProtectedCommand, string>, ProtectedCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var tracker = scope.ServiceProvider.GetRequiredService<InvocationTracker>();
        var authorizerTracker = scope.ServiceProvider.GetRequiredService<RoleAuthorizerTracker>();
        var idempotencyStore = scope.ServiceProvider.GetRequiredService<TestCommandIdempotencyStore>();

        var result = await dispatcher.Send(new ProtectedCommand("admin", "request-1", "blocked"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ServiceUnavailable, result.Error.Kind);
        Assert.Equal("module.disabled", result.Error.Code);
        Assert.Equal(0, tracker.ProtectedCommandInvocations);
        Assert.Equal(0, authorizerTracker.InvocationCount);
        Assert.Equal(0, idempotencyStore.BeginCount);
    }

    public sealed record DiscoveredCommand(string Name) : ICommand<string>;

    private sealed class DiscoveredCommandHandler : ICommandHandler<DiscoveredCommand, string>
    {
        public Task<Result<string>> Handle(DiscoveredCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<string>.Success(command.Name + "-handled"));
        }
    }

    public sealed record DiscoveredQuery(int Value) : IQuery<int>;

    private sealed class DiscoveredQueryHandler : IQueryHandler<DiscoveredQuery, int>
    {
        public Task<Result<int>> Handle(DiscoveredQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<int>.Success(query.Value * 2));
        }
    }

    public sealed record ValidatedCommand(string Value) : ICommand<string>;

    private sealed class ValidatedCommandHandler : ICommandHandler<ValidatedCommand, string>
    {
        private readonly InvocationTracker _tracker;

        public ValidatedCommandHandler(InvocationTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<Result<string>> Handle(ValidatedCommand command, CancellationToken cancellationToken)
        {
            _tracker.ValidatedCommandInvocations++;
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    private sealed class ValidatedCommandValidator : IRequestValidator<ValidatedCommand>
    {
        public Task<IReadOnlyList<Error>> ValidateAsync(ValidatedCommand request, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(request.Value))
            {
                return Task.FromResult<IReadOnlyList<Error>>(Array.Empty<Error>());
            }

            return Task.FromResult<IReadOnlyList<Error>>(
                new[] { new Error("validation.value.required", "Value is required.", ErrorKind.Validation) });
        }
    }

    public sealed record ThrowingCommand : ICommand<string>;

    private sealed class ThrowingCommandHandler : ICommandHandler<ThrowingCommand, string>
    {
        public Task<Result<string>> Handle(ThrowingCommand command, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Boom");
        }
    }

    public sealed record TransactionCommand(string ModuleKey, string Value) : ICommand<string>, IModuleScoped;

    private sealed class TransactionCommandHandler : ICommandHandler<TransactionCommand, string>
    {
        public Task<Result<string>> Handle(TransactionCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    public sealed record TransactionQuery(int Value) : IQuery<int>;

    private sealed class TransactionQueryHandler : IQueryHandler<TransactionQuery, int>
    {
        public Task<Result<int>> Handle(TransactionQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<int>.Success(query.Value * 2));
        }
    }

    public sealed record FailingCommand : ICommand<string>;

    private sealed class FailingCommandHandler : ICommandHandler<FailingCommand, string>
    {
        private readonly TelemetryTracker? _telemetryTracker;

        public FailingCommandHandler(TelemetryTracker? telemetryTracker = null)
        {
            _telemetryTracker = telemetryTracker;
        }

        public Task<Result<string>> Handle(FailingCommand command, CancellationToken cancellationToken)
        {
            _telemetryTracker?.Events.Add("handler");
            return Task.FromResult(Result<string>.Failure(new Error("command.failed", "Command failed.", ErrorKind.Conflict)));
        }
    }

    public sealed record RequestContextCommand(string Value) : ICommand<string>;

    private sealed class RequestContextCommandHandler : ICommandHandler<RequestContextCommand, string>
    {
        private readonly IRequestContextAccessor _requestContextAccessor;
        private readonly RequestContextTracker _tracker;

        public RequestContextCommandHandler(IRequestContextAccessor requestContextAccessor, RequestContextTracker tracker)
        {
            _requestContextAccessor = requestContextAccessor;
            _tracker = tracker;
        }

        public Task<Result<string>> Handle(RequestContextCommand command, CancellationToken cancellationToken)
        {
            _tracker.LastSeenContext = _requestContextAccessor.Current;
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    public sealed record TelemetryCommand(string ModuleKey, string Value) : ICommand<string>, IModuleScoped;

    private sealed class TelemetryCommandHandler : ICommandHandler<TelemetryCommand, string>
    {
        private readonly TelemetryTracker _tracker;

        public TelemetryCommandHandler(TelemetryTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<Result<string>> Handle(TelemetryCommand command, CancellationToken cancellationToken)
        {
            _tracker.Events.Add("handler");
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    public sealed record TelemetryThrowingCommand(string ModuleKey) : ICommand<string>, IModuleScoped;

    private sealed class TelemetryThrowingCommandHandler : ICommandHandler<TelemetryThrowingCommand, string>
    {
        private readonly TelemetryTracker _tracker;

        public TelemetryThrowingCommandHandler(TelemetryTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<Result<string>> Handle(TelemetryThrowingCommand command, CancellationToken cancellationToken)
        {
            _tracker.Events.Add("handler");
            throw new InvalidOperationException("telemetry boom");
        }
    }

    public sealed record ReplayableIdempotentCommand(string RequestKey, string Value) : IIdempotentCommand<string>;

    private sealed class ReplayableIdempotentCommandHandler : ICommandHandler<ReplayableIdempotentCommand, string>
    {
        private readonly InvocationTracker _tracker;

        public ReplayableIdempotentCommandHandler(InvocationTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<Result<string>> Handle(ReplayableIdempotentCommand command, CancellationToken cancellationToken)
        {
            _tracker.ReplayableIdempotentCommandInvocations++;
            return Task.FromResult(Result<string>.Success(command.Value + "-handled"));
        }
    }

    public sealed record HandledExceptionCommand : ICommand<string>;

    private sealed class HandledExceptionCommandHandler : ICommandHandler<HandledExceptionCommand, string>
    {
        public Task<Result<string>> Handle(HandledExceptionCommand command, CancellationToken cancellationToken)
        {
            throw new DomainSpecificException("Handled");
        }
    }

    private sealed class HandledExceptionCommandExceptionHandler : IRequestExceptionHandler<HandledExceptionCommand, Result<string>, DomainSpecificException>
    {
        public Task<Result<string>> Handle(HandledExceptionCommand request, DomainSpecificException exception, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<string>.Success("handled-specific-exception"));
        }
    }

    public sealed record ActionCommand : ICommand<string>;

    private sealed class ActionCommandHandler : ICommandHandler<ActionCommand, string>
    {
        public Task<Result<string>> Handle(ActionCommand command, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Trigger action");
        }
    }

    private sealed class ActionCommandExceptionAction : IRequestExceptionAction<ActionCommand, InvalidOperationException>
    {
        private readonly ExceptionActionTracker _tracker;
        private readonly IRequestContextAccessor _requestContextAccessor;

        public ActionCommandExceptionAction(ExceptionActionTracker tracker, IRequestContextAccessor requestContextAccessor)
        {
            _tracker = tracker;
            _requestContextAccessor = requestContextAccessor;
        }

        public Task Execute(ActionCommand request, InvalidOperationException exception, CancellationToken cancellationToken)
        {
            _tracker.ExecutionCount++;
            _tracker.CorrelationId = _requestContextAccessor.Current?.CorrelationId;
            return Task.CompletedTask;
        }
    }

    private sealed class TelemetryExceptionAction : IRequestExceptionAction<TelemetryThrowingCommand, InvalidOperationException>
    {
        private readonly TelemetryTracker _tracker;

        public TelemetryExceptionAction(TelemetryTracker tracker)
        {
            _tracker = tracker;
        }

        public Task Execute(TelemetryThrowingCommand request, InvalidOperationException exception, CancellationToken cancellationToken)
        {
            _tracker.Events.Add("exception-action");
            return Task.CompletedTask;
        }
    }

    public sealed record ModuleScopedCommand(string ModuleKey, string Value) : ICommand<string>, IModuleScoped;

    private sealed class ModuleScopedCommandHandler : ICommandHandler<ModuleScopedCommand, string>
    {
        private readonly InvocationTracker _tracker;

        public ModuleScopedCommandHandler(InvocationTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<Result<string>> Handle(ModuleScopedCommand command, CancellationToken cancellationToken)
        {
            _tracker.ModuleScopedCommandInvocations++;
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    public sealed record ProtectedCommand(string ModuleKey, string RequestKey, string Value)
        : IIdempotentCommand<string>, IModuleScoped, IAuthorizeRequest
    {
        public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
            new[] { RoleRequirement.Admin };
    }

    private sealed class ProtectedCommandHandler : ICommandHandler<ProtectedCommand, string>
    {
        private readonly InvocationTracker _tracker;
        private readonly TelemetryTracker? _telemetryTracker;

        public ProtectedCommandHandler(InvocationTracker tracker, TelemetryTracker? telemetryTracker = null)
        {
            _tracker = tracker;
            _telemetryTracker = telemetryTracker;
        }

        public Task<Result<string>> Handle(ProtectedCommand command, CancellationToken cancellationToken)
        {
            _tracker.ProtectedCommandInvocations++;
            _telemetryTracker?.Events.Add("handler");
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    public sealed record TimeoutSpinningCommand(TimeSpan Timeout) : ICommand<string>, ITimeoutRequest;

    private sealed class TimeoutSpinningCommandHandler : ICommandHandler<TimeoutSpinningCommand, string>
    {
        public async Task<Result<string>> Handle(TimeoutSpinningCommand command, CancellationToken cancellationToken)
        {
            // Use Task.Delay so the handler observes the token via the standard Task cancellation contract.
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            return Result<string>.Success("never");
        }
    }

    public sealed record OrphanProbeCommand(TimeSpan Timeout) : ICommand<string>, ITimeoutRequest;

    private sealed class OrphanProbeCommandHandler : ICommandHandler<OrphanProbeCommand, string>
    {
        private readonly OrphanWorkSentinel _sentinel;

        public OrphanProbeCommandHandler(OrphanWorkSentinel sentinel)
        {
            _sentinel = sentinel;
        }

        public async Task<Result<string>> Handle(OrphanProbeCommand command, CancellationToken cancellationToken)
        {
            // The handler intends to write the sentinel after twice the configured timeout. If the dispatcher correctly
            // propagates the timeout-linked token, Task.Delay throws and the post-delay write never runs.
            await Task.Delay(TimeSpan.FromMilliseconds(command.Timeout.TotalMilliseconds * 2), cancellationToken);
            _sentinel.WasWrittenAfterTimeout = true;
            return Result<string>.Success("done");
        }
    }

    private sealed class OrphanWorkSentinel
    {
        public volatile bool WasWrittenAfterTimeout;
    }

    private sealed class StubExceptionMapper : IExceptionToErrorMapper
    {
        private readonly string _code;
        private readonly ErrorKind _kind;

        public StubExceptionMapper(string code, ErrorKind kind)
        {
            _code = code;
            _kind = kind;
        }

        public Error Map(Exception exception)
        {
            return new Error(_code, exception.Message, _kind);
        }
    }

    private sealed class DomainSpecificException : Exception
    {
        public DomainSpecificException(string message)
            : base(message)
        {
        }
    }

    private sealed class InvocationTracker
    {
        public int ValidatedCommandInvocations { get; set; }

        public int ModuleScopedCommandInvocations { get; set; }

        public int ProtectedCommandInvocations { get; set; }

        public int ReplayableIdempotentCommandInvocations { get; set; }
    }

    private sealed class RoleAuthorizerTracker
    {
        public int InvocationCount { get; set; }
    }

    private sealed class ExceptionActionTracker
    {
        public int ExecutionCount { get; set; }

        public string? CorrelationId { get; set; }
    }

    private sealed class RequestContextTracker
    {
        public RequestContext? LastSeenContext { get; set; }
    }

    private sealed class TelemetryTracker
    {
        public List<string> Events { get; } = [];

        public List<RequestTelemetryContext> Contexts { get; } = [];

        public string? ErrorCode { get; set; }

        public string? ExceptionType { get; set; }
    }

    private sealed class TransactionTracker
    {
        public int BeginCount { get; set; }

        public int CommitCount { get; set; }

        public int RollbackCount { get; set; }

        public List<CommandTransactionContext> Contexts { get; } = [];
    }

    private sealed class TrackingCommandTransactionScopeFactory : ICommandTransactionScopeFactory
    {
        private readonly TransactionTracker _tracker;

        public TrackingCommandTransactionScopeFactory(TransactionTracker tracker)
        {
            _tracker = tracker;
        }

        public ValueTask<ICommandTransactionScope> BeginAsync(CommandTransactionContext context, CancellationToken cancellationToken)
        {
            _tracker.BeginCount++;
            _tracker.Contexts.Add(context);

            return ValueTask.FromResult<ICommandTransactionScope>(new TrackingCommandTransactionScope(_tracker));
        }
    }

    private sealed class TrackingCommandTransactionScope : ICommandTransactionScope
    {
        private readonly TransactionTracker _tracker;

        public TrackingCommandTransactionScope(TransactionTracker tracker)
        {
            _tracker = tracker;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            _tracker.CommitCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            _tracker.RollbackCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingRequestTelemetrySessionFactory : IRequestTelemetrySessionFactory
    {
        private readonly TelemetryTracker _tracker;

        public TrackingRequestTelemetrySessionFactory(TelemetryTracker tracker)
        {
            _tracker = tracker;
        }

        public IRequestTelemetrySession Start(RequestTelemetryContext context)
        {
            _tracker.Events.Add("telemetry-start");
            _tracker.Contexts.Add(context);
            return new TrackingRequestTelemetrySession(_tracker);
        }
    }

    private sealed class TrackingRequestTelemetrySession : IRequestTelemetrySession
    {
        private readonly TelemetryTracker _tracker;

        public TrackingRequestTelemetrySession(TelemetryTracker tracker)
        {
            _tracker = tracker;
        }

        public void Complete()
        {
            _tracker.Events.Add("telemetry-success");
        }

        public void Fail(Error error)
        {
            _tracker.ErrorCode = error.Code;
            _tracker.Events.Add("telemetry-failure");
        }

        public void Fail(Exception exception)
        {
            _tracker.ExceptionType = exception.GetType().FullName;
            _tracker.Events.Add("telemetry-exception");
        }

        public void Cancel()
        {
            _tracker.Events.Add("telemetry-cancelled");
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestCommandIdempotencyRequestHasher : ICommandIdempotencyRequestHasher
    {
        public string ComputeHash<TRequest>(TRequest request)
        {
            return request?.ToString() ?? string.Empty;
        }
    }

    private sealed class TestCommandIdempotencyStore : ICommandIdempotencyStore
    {
        private readonly Dictionary<(Type CommandType, string? ModuleKey, string RequestKey), TestRecord> _records = [];

        public int BeginCount { get; private set; }

        public ValueTask<CommandIdempotencyAcquireResult> BeginAsync(
            Type responseType,
            CommandIdempotencyContext context,
            CancellationToken cancellationToken)
        {
            BeginCount++;

            var key = (context.CommandType, context.ModuleKey, context.RequestKey);

            if (!_records.TryGetValue(key, out var record))
            {
                record = new TestRecord(context.RequestHash);
                _records[key] = record;
                return ValueTask.FromResult(CommandIdempotencyAcquireResult.Acquired(new TestCommandIdempotencyExecution(record)));
            }

            if (!string.Equals(record.RequestHash, context.RequestHash, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    CommandIdempotencyAcquireResult.Reject(
                        CommandIdempotencyErrors.RequestConflict(context.CommandType, context.RequestKey)));
            }

            if (!record.IsCompleted)
            {
                return ValueTask.FromResult(
                    CommandIdempotencyAcquireResult.Reject(
                        CommandIdempotencyErrors.RequestInFlight(context.CommandType, context.RequestKey)));
            }

            return ValueTask.FromResult(CommandIdempotencyAcquireResult.Replay(record.Response!));
        }

        private sealed class TestRecord
        {
            public TestRecord(string requestHash)
            {
                RequestHash = requestHash;
            }

            public string RequestHash { get; }

            public bool IsCompleted { get; set; }

            public object? Response { get; set; }
        }

        private sealed class TestCommandIdempotencyExecution : ICommandIdempotencyExecution
        {
            private readonly TestRecord _record;

            public TestCommandIdempotencyExecution(TestRecord record)
            {
                _record = record;
            }

            public ValueTask CompleteAsync(object response, CancellationToken cancellationToken)
            {
                _record.IsCompleted = true;
                _record.Response = response;
                return ValueTask.CompletedTask;
            }

            public ValueTask AbandonAsync(CancellationToken cancellationToken)
            {
                _record.IsCompleted = false;
                _record.Response = null;
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TestCurrentActorAccessor : ICurrentActorAccessor
    {
        private readonly CurrentActor _actor;

        public TestCurrentActorAccessor(CurrentActor actor)
        {
            _actor = actor;
        }

        public ValueTask<CurrentActor> GetCurrentAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_actor);
        }
    }

    private sealed class TrackingRoleAuthorizer : IRoleAuthorizer
    {
        private readonly RoleAuthorizerTracker _tracker;

        public TrackingRoleAuthorizer(RoleAuthorizerTracker tracker)
        {
            _tracker = tracker;
        }

        public ValueTask<Error> GetFailureOrNoneAsync(
            Type requestType,
            CurrentActor actor,
            IReadOnlyCollection<RoleRequirement> requirements,
            CancellationToken cancellationToken)
        {
            _tracker.InvocationCount++;
            return ValueTask.FromResult(Error.None);
        }
    }

    private sealed class StubModuleStateGuard : IModuleStateGuard
    {
        private readonly Error _error;

        public StubModuleStateGuard(Error error)
        {
            _error = error;
        }

        public ValueTask<Error> GetFailureOrNoneAsync(string moduleKey, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_error);
        }
    }
}
