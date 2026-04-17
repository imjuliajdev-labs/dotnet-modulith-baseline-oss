using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class ServiceRegistrationBoundaryTests
{
    [Fact]
    public void ModuleServiceRegistrationCodeReferencesOtherModulesOnlyThroughPublicContracts()
    {
        var moduleNames = RepositoryFiles.ReadModuleNames();
        var registrationFiles = moduleNames
            .SelectMany(GetRegistrationFilesForModule)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        foreach (var relativePath in registrationFiles)
        {
            var owningModule = GetOwningModule(relativePath);
            var text = RepositoryFiles.ReadAllText(relativePath);

            foreach (var otherModule in moduleNames.Where(moduleName => !string.Equals(moduleName, owningModule, StringComparison.Ordinal)))
            {
                var forbiddenCrossModuleReference = $@"\b{Regex.Escape(otherModule)}\.(?!PublicContracts\b)";
                Assert.DoesNotMatch(forbiddenCrossModuleReference, text);
            }
        }
    }

    private static IEnumerable<string> GetRegistrationFilesForModule(string moduleName)
    {
        var apiDirectory = RepositoryFiles.PathFromRoot("src", "Modules", moduleName, $"{moduleName}.Api");
        var infrastructureDirectory = RepositoryFiles.PathFromRoot("src", "Modules", moduleName, $"{moduleName}.Infrastructure");

        foreach (var fullPath in Directory.GetFiles(apiDirectory, "*Module.cs", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.GetRelativePath(RepositoryFiles.Root, fullPath).Replace('\\', '/');
        }

        foreach (var fullPath in Directory.GetFiles(infrastructureDirectory, "*ServiceCollectionExtensions.cs", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.GetRelativePath(RepositoryFiles.Root, fullPath).Replace('\\', '/');
        }
    }

    private static string GetOwningModule(string relativePath)
    {
        var segments = relativePath.Split('/');
        return segments[2];
    }
}
