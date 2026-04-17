using Architecture.Tests.Support;

namespace Architecture.Tests;

public sealed class ModuleDependencyReportTests
{
    [Fact]
    public void Report_output_is_deterministic_and_sorted()
    {
        var first = ModuleDependencyReportInventory.Build();
        var second = ModuleDependencyReportInventory.Build();

        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < first.Count; index++)
        {
            Assert.Equal(first[index].Module, second[index].Module);
            Assert.Equal(first[index].Refs, second[index].Refs);
            Assert.Equal(first[index].Headroom, second[index].Headroom);
            Assert.Equal(first[index].References, second[index].References);
        }

        var sortedModuleNames = first.Select(row => row.Module).ToArray();
        Assert.Equal(
            sortedModuleNames.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            sortedModuleNames);
    }

    [Fact]
    public void No_module_exceeds_the_cross_module_reference_cap()
    {
        var rows = ModuleDependencyReportInventory.Build();

        var violations = rows
            .Where(row => row.Refs > ModuleDependencyReportInventory.CrossModulePublicContractsReferenceCap)
            .Select(row => $"{row.Module} references {row.Refs} other modules' PublicContracts (cap: {ModuleDependencyReportInventory.CrossModulePublicContractsReferenceCap})")
            .ToArray();

        Assert.Empty(violations);
    }
}
