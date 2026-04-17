using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Modules;

namespace BuildingBlocks.Infrastructure.Modules;

public sealed class ModuleStateGuard : IModuleStateGuard
{
    private readonly IModuleStateReader _moduleStateReader;

    public ModuleStateGuard(IModuleStateReader moduleStateReader)
    {
        _moduleStateReader = moduleStateReader ?? throw new ArgumentNullException(nameof(moduleStateReader));
    }

    public async ValueTask<Error> GetFailureOrNoneAsync(string moduleKey, CancellationToken cancellationToken)
    {
        var state = await _moduleStateReader.GetRequiredStateAsync(moduleKey, cancellationToken);
        return state.RuntimeState == ModuleRuntimeState.Enabled
            ? Error.None
            : ModuleStateErrors.NotEnabled(state.ModuleKey, state.RuntimeState);
    }
}
