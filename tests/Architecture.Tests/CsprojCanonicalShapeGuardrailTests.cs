using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class CsprojCanonicalShapeGuardrailTests
{
    private static readonly string[] RedundantInheritedProperties =
    [
        "TargetFramework",
        "ImplicitUsings",
        "Nullable"
    ];

    [Fact]
    public void ModuleCsprojFilesUseSingleBackslashProjectReferences()
    {
        var offenders = EnumerateModuleCsprojFiles()
            .Where(ContainsDoubleBackslashProjectReference)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            BuildMessage(
                "The following .csproj files contain escaped double-backslash (\"..\\\\..\\\\\") path separators in ProjectReference Include attributes. Use canonical single-backslash separators (\"..\\..\\\") to match Platform/Identity/Admin/SampleFeature.",
                offenders));
    }

    [Fact]
    public void ModuleCsprojFilesDoNotRedeclareDirectoryBuildPropsProperties()
    {
        var offenders = EnumerateModuleCsprojFiles()
            .Select(path => (Path: path, Redundant: RedundantPropertiesIn(path)))
            .Where(static pair => pair.Redundant.Length > 0)
            .OrderBy(static pair => pair.Path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            BuildMessage(
                "The following module .csproj files redeclare properties that are already set by Directory.Build.props (TargetFramework, ImplicitUsings, Nullable). Remove the project-level <PropertyGroup> entries.",
                offenders.Select(pair => $"{pair.Path} → {string.Join(", ", pair.Redundant)}")));
    }

    private static IEnumerable<string> EnumerateModuleCsprojFiles()
    {
        var modulesRoot = RepositoryFiles.PathFromRoot("src", "Modules");
        return Directory.GetFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(ToRepoRelative);
    }

    private static bool ContainsDoubleBackslashProjectReference(string relativePath)
    {
        var bytes = File.ReadAllBytes(RepositoryFiles.PathFromRoot(relativePath.Split('/')));
        // A literal "\\" inside a ProjectReference Include attribute appears as two consecutive
        // 0x5C bytes in UTF-8. We read raw bytes so that nothing in this pipeline normalizes them
        // away before the assertion (Path.GetFullPath, XML parsers, and Regex all coerce escape
        // forms silently — a byte-level check is the only honest gate).
        for (var index = 0; index < bytes.Length - 1; index++)
        {
            if (bytes[index] == 0x5C && bytes[index + 1] == 0x5C)
            {
                return true;
            }
        }

        return false;
    }

    private static string[] RedundantPropertiesIn(string relativePath)
    {
        var text = File.ReadAllText(RepositoryFiles.PathFromRoot(relativePath.Split('/')));
        return RedundantInheritedProperties
            .Where(property => Regex.IsMatch(text, $"<{property}>[^<]+</{property}>"))
            .ToArray();
    }

    private static string ToRepoRelative(string absolutePath)
    {
        var rootWithSeparator = RepositoryFiles.Root.EndsWith(Path.DirectorySeparatorChar)
            ? RepositoryFiles.Root
            : RepositoryFiles.Root + Path.DirectorySeparatorChar;
        var relative = absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? absolutePath[rootWithSeparator.Length..]
            : absolutePath;
        return relative.Replace('\\', '/');
    }

    private static string BuildMessage(string header, IEnumerable<string> lines)
    {
        var body = string.Join(Environment.NewLine, lines.Select(line => $"  - {line}"));
        return $"{header}{Environment.NewLine}{body}";
    }
}
