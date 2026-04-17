using BuildingBlocks.Application.Modules;
using BuildingBlocks.Domain.Modules;

namespace BuildingBlocks.Infrastructure.Modules;

public sealed class DescriptorBackedModuleStateReader : IModuleStateReader
{
    private readonly IReadOnlyDictionary<string, ModuleStateSnapshot> _moduleStates;

    public DescriptorBackedModuleStateReader(IEnumerable<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var duplicates = modules
            .GroupBy(static module => module.Key, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate module registrations were found for: {string.Join(", ", duplicates.OrderBy(static key => key, StringComparer.Ordinal))}.");
        }

        _moduleStates = modules.ToDictionary(
            static module => module.Key,
            static module => new ModuleStateSnapshot(
                module.Key,
                module.Descriptor.DefaultEnabled ? ModuleRuntimeState.Enabled : ModuleRuntimeState.Disabled),
            StringComparer.Ordinal);
    }

    public ValueTask<ModuleStateSnapshot> GetRequiredStateAsync(string moduleKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        if (_moduleStates.TryGetValue(moduleKey, out var state))
        {
            return ValueTask.FromResult(state);
        }

        throw new InvalidOperationException($"No module state registration exists for module '{moduleKey}'.");
    }
}
