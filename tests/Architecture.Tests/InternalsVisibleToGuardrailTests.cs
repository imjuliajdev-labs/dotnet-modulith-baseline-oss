using System.Reflection;
using System.Runtime.CompilerServices;

namespace Architecture.Tests;

public sealed class InternalsVisibleToGuardrailTests
{
    private static readonly IReadOnlySet<string> AllowedTestAssemblies = new HashSet<string>(StringComparer.Ordinal)
    {
        "Module.UnitTests",
        "Architecture.Tests",
        "Integration.Tests"
    };

    [Fact]
    public void ProductionAssembliesOnlyGrantInternalsToApprovedTestAssemblies()
    {
        var productionAssemblies = LoadProductionAssemblies();

        Assert.NotEmpty(productionAssemblies);

        var violations = new List<string>();
        foreach (var assembly in productionAssemblies)
        {
            foreach (var attribute in assembly.GetCustomAttributes<InternalsVisibleToAttribute>())
            {
                var grantedTo = StripPublicKeyToken(attribute.AssemblyName);
                if (!AllowedTestAssemblies.Contains(grantedTo))
                {
                    violations.Add(
                        $"{assembly.GetName().Name} grants InternalsVisibleTo to '{grantedTo}', which is not an approved test assembly. Allowed: {string.Join(", ", AllowedTestAssemblies.OrderBy(static name => name, StringComparer.Ordinal))}.");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"InternalsVisibleTo violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static IReadOnlyList<Assembly> LoadProductionAssemblies()
    {
        var moduleNames = RepositoryFiles.ReadModuleNames();
        var suffixes = new[] { "Api", "Application", "Domain", "Infrastructure", "PublicContracts" };

        var moduleAssemblies = moduleNames
            .SelectMany(moduleName => suffixes.Select(suffix => $"{moduleName}.{suffix}"))
            .Select(TryLoadAssembly)
            .Where(static assembly => assembly is not null)
            .Cast<Assembly>();

        var buildingBlocks = new[]
        {
            "BuildingBlocks.Domain",
            "BuildingBlocks.Application",
            "BuildingBlocks.Infrastructure"
        }
            .Select(TryLoadAssembly)
            .Where(static assembly => assembly is not null)
            .Cast<Assembly>();

        return moduleAssemblies
            .Concat(buildingBlocks)
            .DistinctBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static Assembly? TryLoadAssembly(string assemblyName)
    {
        try
        {
            return Assembly.Load(assemblyName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static string StripPublicKeyToken(string assemblyName)
    {
        var commaIndex = assemblyName.IndexOf(',', StringComparison.Ordinal);
        return commaIndex < 0 ? assemblyName : assemblyName[..commaIndex].Trim();
    }
}
