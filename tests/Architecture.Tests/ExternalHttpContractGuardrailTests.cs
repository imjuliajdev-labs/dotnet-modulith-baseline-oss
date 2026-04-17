using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class ExternalHttpContractGuardrailTests
{
    private static readonly Regex TypeDeclarationPattern = new(
        @"\b(?:record|class)\s+([A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void ExternalHttpContractsUseVersionedNamesInsideApiContractsFolders()
    {
        var contractFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains(".Api/Contracts/", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(contractFiles, static path => path.EndsWith("Placeholder.cs", StringComparison.Ordinal));

        foreach (var contractFile in contractFiles)
        {
            var source = RepositoryFiles.ReadAllText(contractFile);

            Assert.Contains("namespace ", source, StringComparison.Ordinal);
            Assert.Contains(".Api.Contracts", source, StringComparison.Ordinal);

            var declaredTypes = TypeDeclarationPattern.Matches(source)
                .Select(static match => match.Groups[1].Value)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            Assert.NotEmpty(declaredTypes);
            Assert.All(
                declaredTypes,
                typeName => Assert.Matches(@".*V\d+$", typeName));
        }
    }
}
