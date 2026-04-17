using System.Text.Json;
using System.Text.RegularExpressions;
using Architecture.Tests.Support;

namespace Architecture.Tests;

public sealed class HandlerTestCoverageGuardrailTests
{
    private static readonly Regex HandlerClassPattern = new(
        @"class\s+(\w+)\s*(?::\s*)?[^{;]*?\b(?:ICommandHandler|IQueryHandler|IIntegrationEventHandler|IModuleScopedIntegrationEventHandler)\s*<",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex TestClassPattern = new(
        @"class\s+(\w+Tests)\b",
        RegexOptions.CultureInvariant);

    private static readonly IReadOnlySet<string> ExcludedHandlers =
        HandlerCoverageInventory.ExemptedHandlerNames;

    [Fact]
    public void Every_handler_should_have_direct_test_coverage_or_a_governed_integration_coverage_entry()
    {
        var handlerNames = DiscoverHandlerNames();
        var testClassNames = DiscoverTestClassNames();
        var untested = new List<string>();

        foreach (var handlerName in handlerNames)
        {
            if (ExcludedHandlers.Contains(handlerName))
            {
                continue;
            }

            var expectedTestName = $"{handlerName}Tests";
            if (!testClassNames.Contains(expectedTestName))
            {
                untested.Add(handlerName);
            }
        }

        Assert.True(
            untested.Count == 0,
            $"The following handlers have neither a corresponding {{HandlerName}}Tests class nor a governed handler-coverage inventory entry. " +
            $"Add a direct test class or a named integration-coverage inventory entry with justification: {string.Join(", ", untested)}");
    }

    [Fact]
    public void Emits_handler_coverage_artifact()
    {
        var handlerNames = DiscoverHandlerNames();
        var testClassNames = DiscoverTestClassNames();

        var tested = handlerNames
            .Where(name => !HandlerCoverageInventory.ExemptedHandlerNames.Contains(name))
            .Where(name => testClassNames.Contains($"{name}Tests"))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        // The exempted list mirrors the declared inventory so the artifact is a faithful projection
        // of governance state. The companion "Exclusion_list_should_only_contain_existing_handlers"
        // test guards against stale entries.
        var exempted = HandlerCoverageInventory.Exemptions
            .OrderBy(static entry => entry.Handler, StringComparer.Ordinal)
            .Select(static entry => new
            {
                handler = entry.Handler,
                reason = entry.Reason,
                coveringTests = entry.CoveringTests
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray(),
                trackedBy = entry.TrackedBy
            })
            .ToArray();

        var report = new
        {
            generatedAt = NodaTime.SystemClock.Instance.GetCurrentInstant().ToDateTimeOffset().ToString("o"),
            totalHandlers = handlerNames.Count,
            tested,
            exempted
        };

        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var artifactPath = RepositoryFiles.PathFromRoot("artifacts", "handler-coverage.json");
        var artifactDirectory = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException($"Could not resolve the directory for '{artifactPath}'.");
        Directory.CreateDirectory(artifactDirectory);

        File.WriteAllText(artifactPath, JsonSerializer.Serialize(report, serializerOptions));

        Assert.True(File.Exists(artifactPath), $"Expected handler coverage artifact at '{artifactPath}'.");
    }

    [Fact]
    public void Exempted_handlers_must_name_an_existing_covering_integration_test()
    {
        var integrationTestClassNames = DiscoverIntegrationTestClassNames();

        var brokenLinks = new List<string>();

        foreach (var exemption in HandlerCoverageInventory.Exemptions)
        {
            if (exemption.CoveringTests.Count == 0)
            {
                brokenLinks.Add($"{exemption.Handler}: declares no covering integration test");
                continue;
            }

            foreach (var coveringTest in exemption.CoveringTests)
            {
                if (!integrationTestClassNames.Contains(coveringTest))
                {
                    brokenLinks.Add($"{exemption.Handler} -> {coveringTest} (not found under tests/Integration.Tests/)");
                }
            }
        }

        Assert.True(
            brokenLinks.Count == 0,
            "Every BP-031 exemption must name an integration test class that actually exists. " +
            "Update HandlerCoverageInventory.cs to point at the current covering test, or remove the exemption " +
            "and add a real handler unit test. Broken links: " + string.Join("; ", brokenLinks));
    }

    [Fact]
    public void Exempted_handlers_must_be_dispatched_by_a_cited_test()
    {
        // BP-031 dispatch verification. The previous Exempted_handlers_must_name_an_existing_covering_integration_test
        // fact only proved the cited class name existed under tests/Integration.Tests/.
        // This stronger fact uses HandlerDispatchReachability to prove every cited test
        // class file has at least one structural reference to the handler — its message
        // type, its class name, or a using-directive importing the handler's namespace —
        // after stripping comments and string literals. Name-only references in prose do
        // not count.
        var moduleSourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules");
        var integrationTestFiles = RepositoryFiles.ReadCSharpFilesUnder("tests", "Integration.Tests");

        var handlerSourceMap = HandlerDispatchReachability.BuildHandlerSourceMap(
            moduleSourceFiles,
            RepositoryFiles.ReadAllText);
        var testClassFileMap = HandlerDispatchReachability.BuildIntegrationTestClassFileMap(
            integrationTestFiles,
            RepositoryFiles.ReadAllText);

        // Cache fingerprinted test files so the per-exemption checks are O(unique files).
        var fingerprints = new Dictionary<string, HandlerDispatchReachability.TestFileFingerprint>(StringComparer.Ordinal);

        HandlerDispatchReachability.TestFileFingerprint Fingerprint(string filePath)
        {
            if (!fingerprints.TryGetValue(filePath, out var cached))
            {
                cached = HandlerDispatchReachability.Fingerprint(filePath, RepositoryFiles.ReadAllText(filePath));
                fingerprints[filePath] = cached;
            }

            return cached;
        }

        var failures = new List<string>();

        foreach (var exemption in HandlerCoverageInventory.Exemptions)
        {
            if (!handlerSourceMap.TryGetValue(exemption.Handler, out var handlerInfo))
            {
                failures.Add(
                    $"{exemption.Handler}: handler source could not be located under src/Modules/. " +
                    "If the handler was renamed or removed, update the inventory.");
                continue;
            }

            foreach (var coveringTest in exemption.CoveringTests)
            {
                if (!testClassFileMap.TryGetValue(coveringTest, out var testFilePath))
                {
                    failures.Add($"{exemption.Handler} -> {coveringTest}: test class not found under tests/Integration.Tests/.");
                    continue;
                }

                var fingerprint = Fingerprint(testFilePath);
                if (!HandlerDispatchReachability.IsHandlerReachable(fingerprint, handlerInfo))
                {
                    failures.Add(
                        $"{exemption.Handler} -> {coveringTest} ({testFilePath}): " +
                        $"the cited test class does not structurally reference '{handlerInfo.MessageType}', " +
                        $"'{handlerInfo.HandlerName}', or import '{handlerInfo.Namespace}'. " +
                        "Update the citation to a test class that genuinely exercises the handler, " +
                        "add a real reference, or remove the exemption.");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "BP-031 dispatch verification failed for one or more handler exemptions. " +
            "Each line below names a citation that does not prove the handler is exercised: "
                + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Exclusion_list_should_only_contain_existing_handlers()
    {
        var handlerNames = DiscoverHandlerNames();

        var staleExclusions = ExcludedHandlers
            .Where(name => !handlerNames.Contains(name))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            staleExclusions.Length == 0,
            $"The exclusion list contains handler names that no longer exist in module source. Remove stale entries: {string.Join(", ", staleExclusions)}");
    }

    private static HashSet<string> DiscoverHandlerNames()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules");
        var handlerNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in sourceFiles.Where(static path =>
            path.Contains(".Application/", StringComparison.Ordinal)))
        {
            var text = RepositoryFiles.ReadAllText(path);
            foreach (Match match in HandlerClassPattern.Matches(text))
            {
                handlerNames.Add(match.Groups[1].Value);
            }
        }

        return handlerNames;
    }

    private static HashSet<string> DiscoverTestClassNames()
    {
        var testClassNames = new HashSet<string>(StringComparer.Ordinal);

        var testDirectories = new[] { "Module.UnitTests", "Integration.Tests" };

        foreach (var testDirectory in testDirectories)
        {
            CollectTestClassNamesFrom(testDirectory, testClassNames);
        }

        return testClassNames;
    }

    private static HashSet<string> DiscoverIntegrationTestClassNames()
    {
        var integrationTestClassNames = new HashSet<string>(StringComparer.Ordinal);
        CollectTestClassNamesFrom("Integration.Tests", integrationTestClassNames);
        return integrationTestClassNames;
    }

    private static void CollectTestClassNamesFrom(string testDirectory, HashSet<string> sink)
    {
        var testFiles = RepositoryFiles.ReadCSharpFilesUnder("tests", testDirectory);
        foreach (var path in testFiles)
        {
            var text = RepositoryFiles.ReadAllText(path);
            foreach (Match match in TestClassPattern.Matches(text))
            {
                sink.Add(match.Groups[1].Value);
            }
        }
    }
}
