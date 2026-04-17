namespace Architecture.Tests;

public sealed class CrossModuleReferenceCapGuardrailTests
{
    private const int MaxCrossModulePublicContractsReferences = 3;

    [Fact]
    public void NoModuleExceedsTheCrossModulePublicContractsReferenceCap()
    {
        var moduleProjects = RepositoryFiles.ReadProjectsUnder("src", "Modules");

        var violatingModules = new List<string>();

        foreach (var moduleName in RepositoryFiles.ReadModuleNames())
        {
            var crossModuleCount = moduleProjects
                .Where(project => GetModuleName(project.RelativePath) == moduleName)
                .SelectMany(project => project.ProjectReferences)
                .Where(reference => reference.StartsWith("src/Modules/", StringComparison.Ordinal))
                .Where(reference => reference.EndsWith(".PublicContracts.csproj", StringComparison.Ordinal))
                .Where(reference => !string.Equals(GetModuleName(reference), moduleName, StringComparison.Ordinal))
                .Select(reference => GetModuleName(reference))
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (crossModuleCount > MaxCrossModulePublicContractsReferences)
            {
                violatingModules.Add($"{moduleName} references {crossModuleCount} other modules' PublicContracts (cap: {MaxCrossModulePublicContractsReferences})");
            }
        }

        Assert.Empty(violatingModules);
    }

    private static string GetModuleName(string relativePath)
    {
        var parts = relativePath.Split('/');
        return parts[2];
    }
}
