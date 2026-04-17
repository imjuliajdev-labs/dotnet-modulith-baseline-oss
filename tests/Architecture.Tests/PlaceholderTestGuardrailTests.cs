using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class PlaceholderTestGuardrailTests
{
    [Fact]
    public void TestProjectsDoNotContainScaffoldPlaceholderTests()
    {
        var testFiles = RepositoryFiles.ReadCSharpFilesUnder("tests")
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal))
            .Where(path =>
                path.StartsWith("tests/Architecture.Tests/Modules/", StringComparison.Ordinal)
                || path.StartsWith("tests/Integration.Tests/Modules/", StringComparison.Ordinal)
                || path.StartsWith("tests/Module.UnitTests/", StringComparison.Ordinal))
            .ToArray();

        var forbiddenTokens = new[]
        {
            "ArchitecturePlaceholderTests",
            "IntegrationPlaceholderTests",
            "UnitPlaceholderTests",
            "public void Placeholder()",
            "Xunit.Assert.True(true);"
        };

        foreach (var relativePath in testFiles)
        {
            var text = RepositoryFiles.ReadAllText(relativePath);
            foreach (var token in forbiddenTokens)
            {
                Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ModuleCoverageTestFilesContainExecutableFactsOrTheories()
    {
        var testFiles = RepositoryFiles.ReadCSharpFilesUnder("tests")
            .Where(path => path.EndsWith("Tests.cs", StringComparison.Ordinal))
            .Where(path =>
                path.StartsWith("tests/Architecture.Tests/Modules/", StringComparison.Ordinal)
                || path.StartsWith("tests/Integration.Tests/Modules/", StringComparison.Ordinal)
                || path.StartsWith("tests/Module.UnitTests/", StringComparison.Ordinal))
            .ToArray();

        var executableTestAttributePattern = new Regex(@"\[(Xunit\.)?(Fact|Theory)\b", RegexOptions.CultureInvariant);
        var filesWithoutExecutableTests = testFiles
            .Where(relativePath => !executableTestAttributePattern.IsMatch(RepositoryFiles.ReadAllText(relativePath)))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            filesWithoutExecutableTests.Length == 0,
            $"Module coverage test files must contain at least one executable Fact or Theory. Files missing test attributes: {string.Join(", ", filesWithoutExecutableTests)}");
    }
}
