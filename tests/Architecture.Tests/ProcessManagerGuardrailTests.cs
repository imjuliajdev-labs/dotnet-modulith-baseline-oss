namespace Architecture.Tests;

public sealed class ProcessManagerGuardrailTests
{
    [Fact]
    public void ProcessManagersRequireDurableStateAndRecoveryCoverage()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules");
        var testFiles = RepositoryFiles.ReadCSharpFilesUnder("tests");

        foreach (var moduleDirectory in Directory.GetDirectories(RepositoryFiles.PathFromRoot("src", "Modules")))
        {
            var moduleName = Path.GetFileName(moduleDirectory);
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                continue;
            }

            var processManagerFiles = sourceFiles
                .Where(path => path.StartsWith($"src/Modules/{moduleName}/{moduleName}.Application/ProcessManagers/", StringComparison.Ordinal))
                .ToArray();

            if (processManagerFiles.Length == 0)
            {
                continue;
            }

            Assert.DoesNotContain(processManagerFiles, static path => path.EndsWith("ProcessManagerPlaceholder.cs", StringComparison.Ordinal));

            var processManagerInfrastructureFiles = sourceFiles
                .Where(path => path.StartsWith($"src/Modules/{moduleName}/{moduleName}.Infrastructure/ProcessManagers/", StringComparison.Ordinal)
                    || path.StartsWith($"src/Modules/{moduleName}/{moduleName}.Infrastructure/Persistence/ProcessManagers/", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(processManagerInfrastructureFiles);
            Assert.DoesNotContain(processManagerInfrastructureFiles, static path => path.EndsWith("Placeholder.cs", StringComparison.Ordinal));

            var recoveryTestFiles = testFiles
                .Where(path => path.StartsWith($"tests/Integration.Tests/Modules/{moduleName}/ProcessManagers/", StringComparison.Ordinal))
                .Where(path => path.EndsWith("ProcessManagerIntegrationTests.cs", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(recoveryTestFiles);
            Assert.All(recoveryTestFiles, path =>
            {
                var source = RepositoryFiles.ReadAllText(path);
                Assert.DoesNotContain("namespace Integration.Tests.ModuleCoverage.", source, StringComparison.Ordinal);
                Assert.DoesNotContain("ProcessManagerShellCarriesTheOwningModuleKey", source, StringComparison.Ordinal);
            });
        }
    }
}
