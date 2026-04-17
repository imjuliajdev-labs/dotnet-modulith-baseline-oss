using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Modules;

namespace BuildingBlocks.Application.Dispatching.Behaviors;

internal sealed class CommandIdempotencyBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
{
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IServiceProvider _serviceProvider;

    public CommandIdempotencyBehavior(IServiceProvider serviceProvider, ICurrentActorAccessor currentActorAccessor)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IIdempotencyRequest idempotencyRequest)
        {
            return await next(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(idempotencyRequest.RequestKey))
        {
            return DispatcherResponseFactory.CreateFailure<TResponse>(CommandIdempotencyErrors.RequestKeyRequired(typeof(TRequest)));
        }

        var hasher = _serviceProvider.GetService(typeof(ICommandIdempotencyRequestHasher)) as ICommandIdempotencyRequestHasher
            ?? throw new InvalidOperationException(
                $"No {nameof(ICommandIdempotencyRequestHasher)} is registered for opted-in idempotent command {typeof(TRequest).FullName}.");

        var store = _serviceProvider.GetService(typeof(ICommandIdempotencyStore)) as ICommandIdempotencyStore
            ?? throw new InvalidOperationException(
                $"No {nameof(ICommandIdempotencyStore)} is registered for opted-in idempotent command {typeof(TRequest).FullName}.");

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var context = new CommandIdempotencyContext(
            typeof(TRequest),
            idempotencyRequest.RequestKey,
            hasher.ComputeHash(request),
            request is IModuleScoped moduleScopedRequest ? moduleScopedRequest.ModuleKey : null,
            actor.IsAuthenticated ? actor.ActorId : null);

        var acquireResult = await store.BeginAsync(typeof(TResponse), context, cancellationToken);
        switch (acquireResult.Status)
        {
            case CommandIdempotencyAcquireStatus.Replay:
                return acquireResult.Response is TResponse replayResponse
                    ? replayResponse
                    : throw new InvalidOperationException(
                        $"Stored idempotent response for {typeof(TRequest).FullName} was not assignable to {typeof(TResponse).FullName}.");

            case CommandIdempotencyAcquireStatus.Reject:
                return DispatcherResponseFactory.CreateFailure<TResponse>(acquireResult.Error);

            case CommandIdempotencyAcquireStatus.Acquired:
                if (acquireResult.Execution is null)
                {
                    throw new InvalidOperationException(
                        $"Idempotency acquire result for {typeof(TRequest).FullName} did not include an execution handle.");
                }

                await using (acquireResult.Execution)
                {
                    try
                    {
                        var response = await next(cancellationToken);
                        await acquireResult.Execution.CompleteAsync(response!, CancellationToken.None);
                        return response;
                    }
                    catch
                    {
                        await acquireResult.Execution.AbandonAsync(CancellationToken.None);
                        throw;
                    }
                }

            default:
                throw new InvalidOperationException(
                    $"Unknown command idempotency acquire status '{acquireResult.Status}'.");
        }
    }
}
