using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class SharedContractCompatibilityGuardrailTests
{
    [Fact]
    public void SharedQueryContractProvidersRequireProviderCompatibilityCoverage()
    {
        foreach (var moduleName in EnumerateModuleNames())
        {
            var contractFiles = EnumerateContractFiles(moduleName, "Queries");
            if (contractFiles.Length == 0)
            {
                continue;
            }

            var testFiles = EnumerateTestFiles(moduleName, "SharedQueries", "*SharedQueryContractTests.cs");
            Assert.Contains(
                testFiles,
                testFile => RepositoryFiles.ReadAllText(testFile).Contains($"using {moduleName}.PublicContracts.Queries;", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SharedQueryContractConsumersRequireCrossModuleCompatibilityCoverage()
    {
        foreach (var (_, consumerModule, contractNamespace) in EnumerateCrossModuleConsumers("Queries"))
        {
            var consumerTestFiles = EnumerateModuleTestFiles(consumerModule);
            Assert.Contains(
                consumerTestFiles,
                testFile => RepositoryFiles.ReadAllText(testFile).Contains($"using {contractNamespace};", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IntegrationEventContractProvidersRequireProviderCompatibilityCoverage()
    {
        foreach (var moduleName in EnumerateModuleNames())
        {
            var contractFiles = EnumerateContractFiles(moduleName, "Events");
            if (contractFiles.Length == 0)
            {
                continue;
            }

            var testFiles = EnumerateTestFiles(moduleName, "Events", "*IntegrationEventContractTests.cs");
            Assert.Contains(
                testFiles,
                testFile => RepositoryFiles.ReadAllText(testFile).Contains($"using {moduleName}.PublicContracts.Events;", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IntegrationEventConsumersRequireCrossModuleCompatibilityCoverage()
    {
        foreach (var (_, consumerModule, contractNamespace) in EnumerateCrossModuleConsumers("Events"))
        {
            var consumerEventTestFiles = EnumerateTestFilesFromPath($"tests/Integration.Tests/Modules/{consumerModule}/Events");
            Assert.Contains(
                consumerEventTestFiles,
                testFile => RepositoryFiles.ReadAllText(testFile).Contains($"using {contractNamespace};", StringComparison.Ordinal));
        }
    }

    private static string[] EnumerateModuleNames()
    {
        return Directory.GetDirectories(RepositoryFiles.PathFromRoot("src", "Modules"))
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] EnumerateContractFiles(string moduleName, string capabilityFolder)
    {
        var contractDirectory = RepositoryFiles.PathFromRoot("src", "Modules", moduleName, $"{moduleName}.PublicContracts", capabilityFolder);
        if (!Directory.Exists(contractDirectory))
        {
            return [];
        }

        return Directory.GetFiles(contractDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.EndsWith("AssemblyMarker.cs", StringComparison.Ordinal))
            .ToArray();
    }

    private static string[] EnumerateTestFiles(string moduleName, string capabilityFolder, string searchPattern)
    {
        var testDirectory = RepositoryFiles.PathFromRoot("tests", "Architecture.Tests", "Modules", moduleName, capabilityFolder);
        if (!Directory.Exists(testDirectory))
        {
            return [];
        }

        return Directory.GetFiles(testDirectory, searchPattern, SearchOption.AllDirectories)
            .Select(ToRepoRelativePath)
            .ToArray();
    }

    private static string[] EnumerateModuleTestFiles(string moduleName)
    {
        return RepositoryFiles.ReadCSharpFilesUnder("tests")
            .Where(path => path.Contains($"/Modules/{moduleName}/", StringComparison.Ordinal))
            .ToArray();
    }

    private static string[] EnumerateTestFilesFromPath(string relativeDirectory)
    {
        var testDirectory = RepositoryFiles.PathFromRoot(relativeDirectory.Split('/'));
        if (!Directory.Exists(testDirectory))
        {
            return [];
        }

        return Directory.GetFiles(testDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(ToRepoRelativePath)
            .ToArray();
    }

    private static IEnumerable<(string ProviderModule, string ConsumerModule, string ContractNamespace)> EnumerateCrossModuleConsumers(string capabilityFolder)
    {
        var namespacePattern = new Regex(
            $@"using\s+(?<provider>[A-Z][A-Za-z0-9]+)\.PublicContracts\.{capabilityFolder};",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        return RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => !path.Contains(".PublicContracts/", StringComparison.Ordinal))
            .SelectMany(relativePath =>
            {
                var consumerModule = relativePath.Split('/')[2];
                return namespacePattern.Matches(RepositoryFiles.ReadAllText(relativePath))
                    .Select(match => match.Groups["provider"].Value)
                    .Where(providerModule => !string.Equals(providerModule, consumerModule, StringComparison.Ordinal))
                    .Select(providerModule => (ProviderModule: providerModule, ConsumerModule: consumerModule, ContractNamespace: $"{providerModule}.PublicContracts.{capabilityFolder}"));
            })
            .GroupBy(static relation => relation)
            .Select(static group => group.Key)
            .OrderBy(static relation => relation.ProviderModule, StringComparer.Ordinal)
            .ThenBy(static relation => relation.ConsumerModule, StringComparer.Ordinal);
    }

    private static string ToRepoRelativePath(string fullPath)
    {
        return Path.GetRelativePath(RepositoryFiles.Root, fullPath).Replace('\\', '/');
    }
}
