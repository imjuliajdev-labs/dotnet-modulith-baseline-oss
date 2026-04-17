namespace Architecture.Tests;

public sealed class IntegrationEventConsumptionGuardrailTests
{
    [Fact]
    public void IntegrationEventConsumersRequireReplayTestsAndDurableReplayGuards()
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

            var consumerFiles = sourceFiles
                .Where(path => path.StartsWith($"src/Modules/{moduleName}/{moduleName}.Application/Consumers/", StringComparison.Ordinal))
                .ToArray();

            if (consumerFiles.Length == 0)
            {
                continue;
            }

            Assert.DoesNotContain(consumerFiles, static path => path.EndsWith("ConsumerPlaceholder.cs", StringComparison.Ordinal));
            Assert.All(consumerFiles, path =>
            {
                var source = RepositoryFiles.ReadAllText(path);
                Assert.Contains("IModuleScoped", source, StringComparison.Ordinal);
            });

            var replayGuardFiles = sourceFiles
                .Where(path => path.StartsWith($"src/Modules/{moduleName}/{moduleName}.Infrastructure/Inbox/", StringComparison.Ordinal)
                    || path.StartsWith($"src/Modules/{moduleName}/{moduleName}.Infrastructure/Deduplication/", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(replayGuardFiles);
            Assert.DoesNotContain(replayGuardFiles, static path => path.EndsWith("Placeholder.cs", StringComparison.Ordinal));

            var replayTestFiles = testFiles
                .Where(path => path.StartsWith($"tests/Integration.Tests/Modules/{moduleName}/Events/", StringComparison.Ordinal))
                .Where(path => path.EndsWith("ConsumerReplayIntegrationTests.cs", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(replayTestFiles);
            Assert.All(replayTestFiles, path =>
            {
                var source = RepositoryFiles.ReadAllText(path);
                Assert.DoesNotContain("namespace Integration.Tests.ModuleCoverage.", source, StringComparison.Ordinal);
                Assert.DoesNotContain("ConsumerShellCarriesTheOwningModuleKey", source, StringComparison.Ordinal);
            });
        }
    }
}
