using BuildingBlocks.Application.Modules;
using BuildingBlocks.Infrastructure.Modules;

namespace Architecture.Tests;

public sealed class ModuleEntryPointTests
{
    [Fact]
    public void EachApiAssemblyExposesExactlyOnePublicModuleEntryPoint()
    {
        var assemblies = RepositoryFiles.ReadModuleApiAssemblies();

        foreach (var assembly in assemblies)
        {
            var exportedModuleTypes = assembly.ExportedTypes
                .Where(type => typeof(IApiModule).IsAssignableFrom(type))
                .Where(type => type is { IsClass: true, IsAbstract: false })
                .ToArray();

            Assert.Single(exportedModuleTypes);
        }
    }

    [Fact]
    public void ModuleEntryPointsUseExpectedAssemblyNames()
    {
        var assemblies = RepositoryFiles.ReadModuleApiAssemblies();

        foreach (var assembly in assemblies)
        {
            var assemblyName = assembly.GetName().Name;
            Assert.False(string.IsNullOrWhiteSpace(assemblyName));
            Assert.EndsWith(".Api", assemblyName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PlatformModuleDeclaresTheLowestCompositionOrder()
    {
        var modules = RepositoryFiles.ReadModuleApiAssemblies()
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(type => typeof(IApiModule).IsAssignableFrom(type))
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Select(type => (IModule)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Could not create module '{type.FullName}'.")))
            .OrderBy(static module => module.Key, StringComparer.Ordinal)
            .ToArray();

        var platformModule = Assert.Single(modules.Where(static module => string.Equals(module.Key, "platform", StringComparison.Ordinal)));

        Assert.Equal(0, platformModule.Descriptor.CompositionOrder);
        Assert.All(
            modules.Where(static module => !string.Equals(module.Key, "platform", StringComparison.Ordinal)),
            module => Assert.True(
                module.Descriptor.CompositionOrder > platformModule.Descriptor.CompositionOrder,
                $"Module '{module.Key}' must compose after the Platform module."));
    }

    [Fact]
    public void ComposedModulesUseDistinctKeysAndSchemaNames()
    {
        var modules = RepositoryFiles.ReadModuleApiAssemblies()
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(type => typeof(IApiModule).IsAssignableFrom(type))
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Select(type => (IModule)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Could not create module '{type.FullName}'.")))
            .OrderBy(static module => module.Key, StringComparer.Ordinal)
            .ToArray();

        var duplicateModuleKeys = modules
            .GroupBy(static module => module.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(static group => group.Key)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();

        var duplicateSchemaNames = modules
            .GroupBy(static module => module.Descriptor.SchemaName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(static group => group.Key)
            .OrderBy(static schemaName => schemaName, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            duplicateModuleKeys.Length == 0,
            $"Duplicate module keys: {string.Join(", ", duplicateModuleKeys)}");
        Assert.True(
            duplicateSchemaNames.Length == 0,
            $"Duplicate schema names: {string.Join(", ", duplicateSchemaNames)}");
    }
}
