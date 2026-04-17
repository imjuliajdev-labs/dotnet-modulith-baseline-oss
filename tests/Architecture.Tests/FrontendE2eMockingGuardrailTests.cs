namespace Architecture.Tests;

/// <summary>
/// End-to-end specs live in <c>web/tests/e2e/</c> and must hit the real backend started
/// by <c>pnpm test:e2e</c>. Mocked API routes via <c>page.route(...)</c> defeat the
/// purpose of the suite: they assert against a fake surface and give false confidence
/// in the real frontend-to-backend contract. Component- and flow-level UI tests that
/// need mocked hooks belong in the Vitest suite under <c>web/tests/unit/</c>.
/// </summary>
public sealed class FrontendE2eMockingGuardrailTests
{
    [Fact]
    public void E2eSpecsDoNotUsePlaywrightPageRouteToMockBackendResponses()
    {
        var e2eDirectory = RepositoryFiles.PathFromRoot("web", "tests", "e2e");

        Assert.True(Directory.Exists(e2eDirectory), $"Expected e2e test directory at {e2eDirectory}.");

        var specFiles = Directory
            .EnumerateFiles(e2eDirectory, "*.spec.ts", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(specFiles);

        var offenders = new List<string>();
        foreach (var path in specFiles)
        {
            var source = File.ReadAllText(path);
            if (source.Contains("page.route(", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(path));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "web/tests/e2e/ specs must hit the real backend. Move mocked UI flow coverage to web/tests/unit/ (Vitest). Offenders: "
            + string.Join(", ", offenders));
    }
}
