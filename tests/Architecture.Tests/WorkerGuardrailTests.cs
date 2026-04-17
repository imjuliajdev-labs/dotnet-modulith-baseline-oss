namespace Architecture.Tests;

public sealed class WorkerGuardrailTests
{
    [Fact]
    public void ModuleWorkersDoNotUseEfCoreMutationApisDirectly()
    {
        var workerFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains(".Infrastructure/Workers/", StringComparison.Ordinal))
            .Where(path => !path.EndsWith("Options.cs", StringComparison.Ordinal))
            .ToArray();

        var forbiddenTokens = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "DbContext",
            ".SaveChanges(",
            ".SaveChangesAsync(",
            ".ExecuteUpdate(",
            ".ExecuteUpdateAsync(",
            ".ExecuteDelete(",
            ".ExecuteDeleteAsync(",
            ".BeginTransaction(",
            ".BeginTransactionAsync("
        };

        var violations = workerFiles
            .SelectMany(path =>
            {
                var source = RepositoryFiles.ReadAllText(path);
                return forbiddenTokens
                    .Where(token => source.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{path}: {token}");
            })
            .ToArray();

        Assert.Empty(violations);
    }
}
