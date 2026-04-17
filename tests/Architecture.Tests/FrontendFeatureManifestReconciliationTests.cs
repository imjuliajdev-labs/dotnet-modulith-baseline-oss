using System.Text.RegularExpressions;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Infrastructure.Modules;

namespace Architecture.Tests;

public sealed class FrontendFeatureManifestReconciliationTests
{
    private static readonly Regex ModuleKeyPattern = new(@"moduleKey:\s*'(?<key>[^']+)'", RegexOptions.CultureInvariant);

    [Fact]
    public void FeatureManifestsMatchModulesThatDeclareFrontendSurface()
    {
        var frontendModuleKeys = RepositoryFiles.ReadModuleApiAssemblies()
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(type => typeof(IApiModule).IsAssignableFrom(type))
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Select(type => (IModule)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Could not create module '{type.FullName}'.")))
            .Select(static module => module.Descriptor)
            .Where(static descriptor => descriptor.HasFrontendSurface)
            .Select(static descriptor => descriptor.Key)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();

        var manifestModuleKeys = Directory.GetDirectories(RepositoryFiles.PathFromRoot("web", "src", "features"))
            .Select(dir => Path.Combine(dir, "index.ts"))
            .Where(File.Exists)
            .Select(ReadModuleKey)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(frontendModuleKeys, manifestModuleKeys);
    }

    [Fact]
    public void FeaturesDirectoryMustContainOnlyManifestedFeatures()
    {
        var featuresRoot = RepositoryFiles.PathFromRoot("web", "src", "features");

        var offenders = Directory.GetDirectories(featuresRoot)
            .Where(directory => !File.Exists(Path.Combine(directory, "index.ts")))
            .Select(directory => Path.GetRelativePath(RepositoryFiles.Root, directory).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Every immediate child of web/src/features/ must contain an index.ts feature manifest (BP-017). Offenders: {string.Join(", ", offenders)}. Non-feature code (app shell, layout, auth) belongs under web/src/shell/.");
    }

    private static string ReadModuleKey(string manifestPath)
    {
        var manifestText = File.ReadAllText(manifestPath);
        var match = ModuleKeyPattern.Match(manifestText);

        Assert.True(match.Success, $"Feature manifest '{Path.GetRelativePath(RepositoryFiles.Root, manifestPath)}' must declare a moduleKey.");
        return match.Groups["key"].Value;
    }
}
