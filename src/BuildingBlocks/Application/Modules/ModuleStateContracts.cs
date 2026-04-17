using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Modules;

namespace BuildingBlocks.Application.Modules;

public sealed record ModuleStateSnapshot(string ModuleKey, ModuleRuntimeState RuntimeState);

public interface IModuleStateReader
{
    ValueTask<ModuleStateSnapshot> GetRequiredStateAsync(string moduleKey, CancellationToken cancellationToken);
}

public interface IModuleStateGuard
{
    ValueTask<Error> GetFailureOrNoneAsync(string moduleKey, CancellationToken cancellationToken);
}

public static class ModuleStateErrors
{
    public static Error NotEnabled(string moduleKey, ModuleRuntimeState runtimeState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        var (code, message) = runtimeState switch
        {
            ModuleRuntimeState.Enabling => (
                "module.enabling",
                $"Module '{moduleKey}' is still enabling and cannot accept requests yet."),
            ModuleRuntimeState.Disabling => (
                "module.disabling",
                $"Module '{moduleKey}' is disabling and no longer accepts new requests."),
            ModuleRuntimeState.Disabled => (
                "module.disabled",
                $"Module '{moduleKey}' is disabled."),
            _ => (
                "module.unavailable",
                $"Module '{moduleKey}' is not currently enabled.")
        };

        return new Error(code, message, ErrorKind.ServiceUnavailable);
    }
}
