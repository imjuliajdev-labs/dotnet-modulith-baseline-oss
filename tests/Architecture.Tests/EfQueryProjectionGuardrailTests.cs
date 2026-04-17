using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed partial class EfQueryProjectionGuardrailTests
{
    [Fact]
    public void ReadSurfaceFilesMustNotUseEagerLoadingInclude()
    {
        var violations = new List<string>();

        foreach (var moduleName in RepositoryFiles.ReadModuleNames())
        {
            var infrastructureFiles = RepositoryFiles
                .ReadCSharpFilesUnder("src", "Modules", moduleName)
                .Where(static path => path.Contains("/Infrastructure/", StringComparison.Ordinal));

            foreach (var filePath in infrastructureFiles)
            {
                var source = RepositoryFiles.ReadAllText(filePath);

                if (!IsReadSurface(source))
                {
                    continue;
                }

                if (IncludeCallRegex().IsMatch(source))
                {
                    violations.Add(
                        $"{filePath}: Uses .Include() on a read surface. " +
                        "Prefer .Select() projections to avoid loading full entity graphs (BP-035).");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static bool IsReadSurface(string source)
    {
        return ReadQueriesInterfaceRegex().IsMatch(source)
            || QueryHandlerInterfaceRegex().IsMatch(source)
            || QueryServiceInterfaceRegex().IsMatch(source);
    }

    [GeneratedRegex(@"\.Include\s*\(", RegexOptions.Compiled)]
    private static partial Regex IncludeCallRegex();

    [GeneratedRegex(@":\s*I\w*ReadQueries\b", RegexOptions.Compiled)]
    private static partial Regex ReadQueriesInterfaceRegex();

    [GeneratedRegex(@"IQueryHandler\s*<", RegexOptions.Compiled)]
    private static partial Regex QueryHandlerInterfaceRegex();

    [GeneratedRegex(@":\s*I\w*(QueryService|Reader)\b", RegexOptions.Compiled)]
    private static partial Regex QueryServiceInterfaceRegex();
}
