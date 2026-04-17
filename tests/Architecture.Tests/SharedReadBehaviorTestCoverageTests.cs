using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class SharedReadBehaviorTestCoverageTests
{
    private static readonly Regex ClassDeclarationPattern = new(
        @"public\s+sealed\s+class\s+(?<name>[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void EveryDeclaredSharedReadAdapterHasARuntimeBehaviorTestFixture()
    {
        var adapterSources = DiscoverSharedReadAdapterSources();
        if (adapterSources.Count == 0)
        {
            return;
        }

        var behaviorTestSources = DiscoverSharedRuntimeBehaviorTestSources();
        Assert.NotEmpty(behaviorTestSources);
        var joinedBehaviorText = string.Join('\n', behaviorTestSources.Values);

        var missing = new List<string>();
        foreach (var (adapterPath, adapterText) in adapterSources)
        {
            var className = ExtractAdapterClassName(adapterText);
            if (className is null)
            {
                continue;
            }

            if (!joinedBehaviorText.Contains(className, StringComparison.Ordinal))
            {
                missing.Add(
                    $"{adapterPath} declares '{className}' with a SharedReadPolicy but no file under tests/Integration.Tests/SharedRuntime/ references it. " +
                    $"Add a behavior test fixture per tests/Architecture.Tests/Support/SharedReadBehaviorTestTemplate.md.");
            }
        }

        Assert.True(
            missing.Count == 0,
            $"Shared read adapters missing behavior tests:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    private static IReadOnlyDictionary<string, string> DiscoverSharedReadAdapterSources()
    {
        var modulesRoot = RepositoryFiles.PathFromRoot("src", "Modules");
        var results = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var moduleDirectory in Directory.GetDirectories(modulesRoot))
        {
            var moduleName = Path.GetFileName(moduleDirectory);
            var sharedReadsDirectory = Path.Combine(moduleDirectory, $"{moduleName}.Application", "SharedReads");
            if (!Directory.Exists(sharedReadsDirectory))
            {
                continue;
            }

            foreach (var filePath in Directory.GetFiles(sharedReadsDirectory, "*.cs", SearchOption.TopDirectoryOnly))
            {
                var text = File.ReadAllText(filePath);
                if (!text.Contains("SharedReadPolicy", StringComparison.Ordinal))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(RepositoryFiles.Root, filePath).Replace('\\', '/');
                results[relative] = text;
            }
        }

        return results;
    }

    private static IReadOnlyDictionary<string, string> DiscoverSharedRuntimeBehaviorTestSources()
    {
        var sharedRuntimeDirectory = RepositoryFiles.PathFromRoot("tests", "Integration.Tests", "SharedRuntime");
        var results = new SortedDictionary<string, string>(StringComparer.Ordinal);

        if (!Directory.Exists(sharedRuntimeDirectory))
        {
            return results;
        }

        foreach (var filePath in Directory.GetFiles(sharedRuntimeDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(RepositoryFiles.Root, filePath).Replace('\\', '/');
            results[relative] = File.ReadAllText(filePath);
        }

        return results;
    }

    private static string? ExtractAdapterClassName(string source)
    {
        foreach (Match match in ClassDeclarationPattern.Matches(source))
        {
            var name = match.Groups["name"].Value;
            if (name.EndsWith("Reader", StringComparison.Ordinal))
            {
                return name;
            }
        }

        return null;
    }
}
