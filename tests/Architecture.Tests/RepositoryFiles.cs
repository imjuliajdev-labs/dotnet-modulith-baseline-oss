using BuildingBlocks.Testing;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Architecture.Tests;

internal static class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static string PathFromRoot(params string[] segments)
    {
        var parts = new string[segments.Length + 1];
        parts[0] = Root;
        Array.Copy(segments, 0, parts, 1, segments.Length);
        return Path.Combine(parts);
    }

    public static IReadOnlyList<RuleCatalogEntry> ReadRuleCatalog()
    {
        var path = PathFromRoot("docs", "RULE_TO_GATE_CATALOG.md");
        return File.ReadAllLines(path)
            .Where(line => line.TrimStart().StartsWith("| BP-", StringComparison.Ordinal))
            .Select(ParseCatalogRow)
            .ToArray();
    }

    public static RuleEnforcementMap ReadRuleEnforcementMap()
    {
        var path = PathFromRoot("docs", "RULE_ENFORCEMENT_MAP.json");
        var map = JsonSerializer.Deserialize<RuleEnforcementMap>(File.ReadAllText(path), JsonOptions());
        return map ?? throw new InvalidOperationException("Failed to deserialize rule enforcement map.");
    }

    public static IReadOnlyList<ProjectFileModel> ReadProjectsUnder(params string[] segments)
    {
        var directoryPath = PathFromRoot(segments);
        return Directory.GetFiles(directoryPath, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(ParseProjectFile)
            .ToArray();
    }

    public static ProjectFileModel ReadProject(params string[] segments)
    {
        return ParseProjectFile(PathFromRoot(segments));
    }

    public static IReadOnlyList<string> ReadCSharpFilesUnder(params string[] segments)
    {
        var directoryPath = PathFromRoot(segments);
        return Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(ToRepoRelativePath)
            .ToArray();
    }

    public static IReadOnlyList<string> ReadModuleNames()
    {
        return Directory.GetDirectories(PathFromRoot("src", "Modules"))
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<Assembly> ReadModuleApiAssemblies()
    {
        return ReadModuleNames()
            .Select(static moduleName => Assembly.Load($"{moduleName}.Api"))
            .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<Assembly> ReadModulePublicContractsAssemblies()
    {
        return ReadModuleNames()
            .Select(static moduleName => Assembly.Load($"{moduleName}.PublicContracts"))
            .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> ReadApprovedBuildingBlocksPublicSurface()
    {
        var path = PathFromRoot("tests", "Architecture.Tests", "Approved", "BuildingBlocks.PublicSurface.approved.txt");
        return File.ReadAllLines(path)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Where(static line => !line.StartsWith("#", StringComparison.Ordinal))
            .ToArray();
    }

    public static IReadOnlyList<string> ReadCurrentBuildingBlocksPublicSurface()
    {
        return ReadBuildingBlocksAssemblies()
            .SelectMany(static assembly => assembly.GetExportedTypes()
                .OrderBy(static type => type.Namespace, StringComparer.Ordinal)
                .ThenBy(static type => GetTypeDisplayName(type), StringComparer.Ordinal))
            .Select(static type => GetQualifiedTypeDisplayName(type))
            .ToArray();
    }

    public static bool IsGovernedRuleEnforcementArtifact(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalizedPath = relativePath.Replace('\\', '/');
        return normalizedPath.Equals("web/eslint.config.js", StringComparison.Ordinal)
            || normalizedPath.StartsWith("scripts/", StringComparison.Ordinal)
            || normalizedPath.StartsWith("tests/Architecture.Tests/", StringComparison.Ordinal)
            || normalizedPath.StartsWith("tests/Integration.Tests/", StringComparison.Ordinal)
            || normalizedPath.StartsWith("web/tests/unit/", StringComparison.Ordinal)
            || normalizedPath.StartsWith("web/tests/e2e/", StringComparison.Ordinal);
    }

    public static string ReadAllText(string relativePath)
    {
        return File.ReadAllText(PathFromRoot(relativePath.Split('/')));
    }

    public static IReadOnlyList<string> ReadBlueprintNonNegotiables()
    {
        var path = PathFromRoot("docs", "BLUEPRINT.md");
        var lines = File.ReadAllLines(path);
        var items = new List<string>();
        var insideSection = false;

        foreach (var line in lines)
        {
            if (!insideSection)
            {
                if (string.Equals(line.Trim(), "## Non-Negotiables", StringComparison.Ordinal))
                {
                    insideSection = true;
                }

                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                items.Add(line[2..].Trim());
            }
        }

        return items;
    }

    public static ScaffoldContract ReadScaffoldContract()
    {
        var path = PathFromRoot("templates", "module", "scaffold.contract.json");
        var contract = JsonSerializer.Deserialize<ScaffoldContract>(File.ReadAllText(path), JsonOptions());
        return contract ?? throw new InvalidOperationException("Failed to deserialize scaffold contract.");
    }

    public static JsonDocument ReadJsonDocument(params string[] segments)
    {
        var path = PathFromRoot(segments);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static IReadOnlyList<Assembly> ReadBuildingBlocksAssemblies()
    {
        return
        [
            typeof(BuildingBlocks.Application.Actors.CurrentActor).Assembly,
            typeof(BuildingBlocks.Domain.Time.IClock).Assembly,
            typeof(BuildingBlocks.Infrastructure.InfrastructureAssemblyMarker).Assembly,
            typeof(TestingAssemblyMarker).Assembly
        ];
    }

    private static string GetQualifiedTypeDisplayName(Type type)
    {
        var displayName = GetTypeDisplayName(type);
        return string.IsNullOrWhiteSpace(type.Namespace)
            ? displayName
            : $"{type.Namespace}.{displayName}";
    }

    private static string GetTypeDisplayName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var tickIndex = type.Name.IndexOf('`');
        var name = tickIndex >= 0 ? type.Name[..tickIndex] : type.Name;
        var arguments = type.GetGenericArguments().Select(GetGenericParameterDisplayName);
        return $"{name}<{string.Join(", ", arguments)}>";
    }

    private static string GetGenericParameterDisplayName(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        return GetQualifiedTypeDisplayName(type);
    }

    private static ProjectFileModel ParseProjectFile(string fullPath)
    {
        var document = XDocument.Load(fullPath);
        var projectDirectory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not resolve the directory for '{fullPath}'.");

        var references = document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => ExpandProjectReferenceGlob(projectDirectory, value!))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var packageReferences = document.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var frameworkReferences = document.Descendants()
            .Where(element => element.Name.LocalName == "FrameworkReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var sdk = document.Root?.Attribute("Sdk")?.Value ?? string.Empty;
        var relativePath = ToRepoRelativePath(fullPath);

        return new ProjectFileModel(
            relativePath,
            Path.GetFileNameWithoutExtension(fullPath),
            sdk,
            references,
            packageReferences,
            frameworkReferences);
    }

    private static RuleCatalogEntry ParseCatalogRow(string line)
    {
        var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
        if (cells.Length != 5)
        {
            throw new InvalidOperationException($"Catalog row has an unexpected number of columns: {line}");
        }

        return new RuleCatalogEntry(cells[0], cells[1], cells[2], cells[3], cells[4]);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    private static string ToRepoRelativePath(string fullPath)
    {
        return Path.GetRelativePath(Root, fullPath).Replace('\\', '/');
    }

    private static IEnumerable<string> ExpandProjectReferenceGlob(string projectDirectory, string includeValue)
    {
        var normalizedInclude = includeValue.Replace('\\', Path.DirectorySeparatorChar);

        if (!normalizedInclude.Contains('*') && !normalizedInclude.Contains('?'))
        {
            yield return ToRepoRelativePath(Path.GetFullPath(Path.Combine(projectDirectory, normalizedInclude)));
            yield break;
        }

        var projectDirectoryRelative = ToRepoRelativePath(projectDirectory);
        var normalizedGlob = NormalizeRelativeGlob(projectDirectoryRelative, includeValue);
        var matcher = BuildGlobRegex(normalizedGlob);

        foreach (var projectPath in Directory.GetFiles(Root, "*.csproj", SearchOption.AllDirectories)
                     .Select(ToRepoRelativePath)
                     .Where(path => matcher.IsMatch(path))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            yield return projectPath;
        }
    }

    private static string NormalizeRelativeGlob(string baseRelativePath, string includeValue)
    {
        var combinedSegments = baseRelativePath
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Concat(includeValue.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));

        var stack = new List<string>();
        foreach (var segment in combinedSegments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(segment);
        }

        return string.Join('/', stack);
    }

    private static Regex BuildGlobRegex(string glob)
    {
        const string doubleStarToken = "__DOUBLE_STAR__";
        var pattern = Regex.Escape(glob.Replace('\\', '/'))
            .Replace("\\*\\*", doubleStarToken)
            .Replace("\\*", "[^/]*")
            .Replace("\\?", "[^/]")
            .Replace(doubleStarToken, ".*");

        return new Regex($"^{pattern}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var readmePath = Path.Combine(current.FullName, "README.md");
            var docsPath = Path.Combine(current.FullName, "docs");
            if (File.Exists(readmePath) && Directory.Exists(docsPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}

internal sealed record RuleCatalogEntry(
    string RuleId,
    string Rule,
    string PrimaryEnforcement,
    string TypicalArtifact,
    string WaiverScope);

internal sealed class RuleEnforcementMap
{
    public string SchemaVersion { get; init; } = string.Empty;

    public Dictionary<string, List<string>> Rules { get; init; } = new(StringComparer.Ordinal);
}

internal sealed class ScaffoldContract
{
    public string SchemaVersion { get; init; } = string.Empty;

    public List<string> RequiredProjectSuffixes { get; init; } = [];

    public List<string> RequiredPaths { get; init; } = [];

    public Dictionary<string, List<string>> OptionalPathsByCapability { get; init; } = new(StringComparer.Ordinal);

    public List<ConditionalScaffoldPathGroup> ConditionalPaths { get; init; } = [];
}

internal sealed class ConditionalScaffoldPathGroup
{
    public List<string> AllOf { get; init; } = [];

    public List<string> Paths { get; init; } = [];
}

internal sealed record ProjectFileModel(
    string RelativePath,
    string ProjectName,
    string Sdk,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> FrameworkReferences);
