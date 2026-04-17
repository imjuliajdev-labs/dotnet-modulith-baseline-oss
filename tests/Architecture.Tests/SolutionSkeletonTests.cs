namespace Architecture.Tests;

public sealed class SolutionSkeletonTests
{
    [Fact]
    public void BackendProjectSkeletonMatchesTheGovernedBlueprint()
    {
        var expectedNonModuleProjects = new[]
        {
            "src/ApiHost/ApiHost.csproj",
            "src/BuildingBlocks/Application/BuildingBlocks.Application.csproj",
            "src/BuildingBlocks/Domain/BuildingBlocks.Domain.csproj",
            "src/BuildingBlocks/Infrastructure/BuildingBlocks.Infrastructure.csproj",
            "src/BuildingBlocks/Testing/BuildingBlocks.Testing.csproj",
            "src/Tools/DbMigrator/DbMigrator.csproj"
        };

        var actualProjects = RepositoryFiles.ReadProjectsUnder("src")
            .Select(project => project.RelativePath)
            .ToArray();

        var actualNonModuleProjects = actualProjects
            .Where(path => !path.StartsWith("src/Modules/", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(expectedNonModuleProjects, actualNonModuleProjects);
        Assert.NotEmpty(actualProjects.Where(path => path.StartsWith("src/Modules/", StringComparison.Ordinal)));
    }

    [Fact]
    public void TestProjectSkeletonExistsForArchitectureIntegrationAndUnitTests()
    {
        var expectedProjects = new[]
        {
            "tests/Architecture.Tests/Architecture.Tests.csproj",
            "tests/Integration.Tests/Integration.Tests.csproj",
            "tests/Module.UnitTests/Module.UnitTests.csproj"
        };

        var actualProjects = RepositoryFiles.ReadProjectsUnder("tests")
            .Select(project => project.RelativePath)
            .Where(path => path.EndsWith(".csproj", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedProjects, actualProjects);
    }

    [Fact]
    public void EachBusinessModuleOwnsExactlyFiveProjects()
    {
        var moduleNames = RepositoryFiles.ReadModuleNames();

        foreach (var moduleName in moduleNames)
        {
            var moduleProjects = RepositoryFiles.ReadProjectsUnder("src", "Modules", moduleName)
                .Select(project => project.ProjectName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            var expectedProjects = new[]
            {
                $"{moduleName}.Api",
                $"{moduleName}.Application",
                $"{moduleName}.Domain",
                $"{moduleName}.Infrastructure",
                $"{moduleName}.PublicContracts"
            };

            Assert.Equal(expectedProjects, moduleProjects);
        }
    }
}
