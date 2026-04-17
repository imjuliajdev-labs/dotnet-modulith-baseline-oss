using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Application.Dispatching.Behaviors;

internal sealed class ModuleStateBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
{
    private readonly IModuleExecutionGate _moduleExecutionGate;

    public ModuleStateBehavior(IModuleExecutionGate moduleExecutionGate)
    {
        _moduleExecutionGate = moduleExecutionGate ?? throw new ArgumentNullException(nameof(moduleExecutionGate));
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IModuleScoped moduleScopedRequest)
        {
            return await next(cancellationToken);
        }

        await using var execution = await _moduleExecutionGate.TryEnterAsync(moduleScopedRequest.ModuleKey, cancellationToken);
        if (!execution.IsEntered)
        {
            return DispatcherResponseFactory.CreateFailure<TResponse>(execution.Failure);
        }

        return await next(cancellationToken);
    }
}
