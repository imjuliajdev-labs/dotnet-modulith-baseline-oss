namespace Architecture.Tests;

public sealed class ModuleBaselineCoverageGuardrailTests
{
    [Fact]
    public void EveryModuleHasBaselineDescriptorBootstrapAndArchitectureCoverage()
    {
        foreach (var moduleName in RepositoryFiles.ReadModuleNames())
        {
            Assert.True(
                File.Exists(RepositoryFiles.PathFromRoot("tests", "Module.UnitTests", moduleName, $"{moduleName}ModuleDescriptorTests.cs")),
                $"Module '{moduleName}' must include tests/Module.UnitTests/{moduleName}/{moduleName}ModuleDescriptorTests.cs.");

            Assert.True(
                File.Exists(RepositoryFiles.PathFromRoot("tests", "Integration.Tests", "Modules", moduleName, $"{moduleName}BootstrapManifestTests.cs")),
                $"Module '{moduleName}' must include tests/Integration.Tests/Modules/{moduleName}/{moduleName}BootstrapManifestTests.cs.");

            Assert.True(
                File.Exists(RepositoryFiles.PathFromRoot("tests", "Architecture.Tests", "Modules", moduleName, $"{moduleName}ArchitectureTests.cs")),
                $"Module '{moduleName}' must include tests/Architecture.Tests/Modules/{moduleName}/{moduleName}ArchitectureTests.cs.");
        }
    }
}
