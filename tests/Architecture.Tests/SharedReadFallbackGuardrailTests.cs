namespace Architecture.Tests;

public sealed class SharedReadFallbackGuardrailTests
{
    [Fact]
    public void AllSharedReadAdaptersDeclareASharedReadPolicyWithExplicitFallback()
    {
        var sharedReadFiles = GetKnownSharedReadFiles();
        if (sharedReadFiles.Count == 0)
        {
            return;
        }

        var violations = new List<string>();

        foreach (var filePath in sharedReadFiles)
        {
            var source = RepositoryFiles.ReadAllText(filePath);

            if (!source.Contains("SharedReadPolicy", StringComparison.Ordinal))
            {
                violations.Add($"{filePath}: Missing SharedReadPolicy declaration.");
                continue;
            }

            if (!source.Contains("SharedReadFallbackBehavior.", StringComparison.Ordinal))
            {
                violations.Add($"{filePath}: SharedReadPolicy does not declare an explicit SharedReadFallbackBehavior.");
                continue;
            }

            if (!source.Contains("Timeout:", StringComparison.Ordinal) &&
                !source.Contains("TimeSpan.From", StringComparison.Ordinal))
            {
                violations.Add($"{filePath}: SharedReadPolicy does not declare an explicit Timeout.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void SharedReadAdaptersWithReturnFallbackBehaviorImplementACatchBlock()
    {
        var violations = new List<string>();

        foreach (var moduleName in RepositoryFiles.ReadModuleNames())
        {
            var sharedReadFiles = RepositoryFiles
                .ReadCSharpFilesUnder("src", "Modules", moduleName)
                .Where(static path => path.Contains("/SharedReads/", StringComparison.Ordinal))
                .Where(static path => !path.Contains("/obj/", StringComparison.Ordinal));

            foreach (var filePath in sharedReadFiles)
            {
                var source = RepositoryFiles.ReadAllText(filePath);

                if (source.Contains("SharedReadFallbackBehavior.ReturnFallback", StringComparison.Ordinal)
                    && !source.Contains("catch", StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{filePath}: Declares ReturnFallback but has no catch block. " +
                        "Adapters using ReturnFallback must catch exceptions and return a safe default.");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static IReadOnlyList<string> GetKnownSharedReadFiles()
    {
        return RepositoryFiles.ReadModuleNames()
            .SelectMany(moduleName => RepositoryFiles
                .ReadCSharpFilesUnder("src", "Modules", moduleName)
                .Where(static path => path.Contains("/SharedReads/", StringComparison.Ordinal))
                .Where(static path => !path.Contains("/obj/", StringComparison.Ordinal)))
            .ToArray();
    }
}
