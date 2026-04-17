namespace Architecture.Tests;

public sealed class IntegrationEventPublicationGuardrailTests
{
    [Fact]
    public void PublishedIntegrationEventsRequireOutboxInfrastructureAndRestartCoverage()
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

            var publishedEventFiles = sourceFiles
                .Where(path => path.StartsWith($"src/Modules/{moduleName}/{moduleName}.PublicContracts/Events/", StringComparison.Ordinal))
                .ToArray();

            if (publishedEventFiles.Length == 0)
            {
                continue;
            }

            var outboxFiles = sourceFiles
                .Where(path => path.StartsWith($"src/Modules/{moduleName}/{moduleName}.Infrastructure/Outbox/", StringComparison.Ordinal)
                    || path.StartsWith($"src/Modules/{moduleName}/{moduleName}.Infrastructure/Persistence/Outbox/", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(outboxFiles);
            Assert.DoesNotContain(outboxFiles, static path => path.EndsWith("Placeholder.cs", StringComparison.Ordinal));

            var outboxIntegrationTests = testFiles
                .Where(path => path.StartsWith($"tests/Integration.Tests/Modules/{moduleName}/Events/", StringComparison.Ordinal))
                .Where(path => path.EndsWith("OutboxIntegrationTests.cs", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(outboxIntegrationTests);
            Assert.All(outboxIntegrationTests, path =>
            {
                var source = RepositoryFiles.ReadAllText(path);
                Assert.DoesNotContain("namespace Integration.Tests.ModuleCoverage.", source, StringComparison.Ordinal);
                Assert.DoesNotContain("GeneratedEventContractCarriesStableMetadata", source, StringComparison.Ordinal);
            });
        }
    }
}
