using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class MaintainedNarrativeDocumentationTests
{
    private const string MaintainedNarrativeMarker = "<!-- doc-tier: maintained-narrative -->";

    private static readonly string[] MaintainedNarrativeDocs =
    [
        "docs/BASELINE_DECLARATION.md",
        "docs/BASELINE_OVERVIEW.md",
        "docs/ARCHITECTURAL_RISKS.md",
        "docs/MODULE_GUIDE.md",
        "docs/REFERENCE_MODULE_TEACHING_MAP.md"
    ];

    [Fact]
    public void DocsIndexDeclaresTheMaintainedNarrativeTier()
    {
        var docsIndex = RepositoryFiles.ReadAllText("docs/README.md");

        Assert.Contains("## Maintained narrative docs", docsIndex, StringComparison.Ordinal);
        Assert.Contains(MaintainedNarrativeMarker, docsIndex, StringComparison.Ordinal);

        foreach (var path in MaintainedNarrativeDocs)
        {
            Assert.Contains(Path.GetFileName(path), docsIndex, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MaintainedNarrativeDocsExistAndCarryTheTierMarker()
    {
        foreach (var relativePath in MaintainedNarrativeDocs)
        {
            var fullPath = RepositoryFiles.PathFromRoot(relativePath.Split('/'));
            Assert.True(File.Exists(fullPath), $"Maintained narrative doc is missing: {relativePath}");

            var text = RepositoryFiles.ReadAllText(relativePath);
            Assert.True(
                text.StartsWith(MaintainedNarrativeMarker, StringComparison.Ordinal),
                $"Maintained narrative doc '{relativePath}' must start with the maintained-doc tier marker.");
        }
    }

    [Fact]
    public void ModuleStateNarrativeDocsDescribeTheConvergedExecutionGatePosture()
    {
        var declaration = RepositoryFiles.ReadAllText("docs/BASELINE_DECLARATION.md");
        var risks = RepositoryFiles.ReadAllText("docs/ARCHITECTURAL_RISKS.md");

        Assert.Contains("shared module-execution gate", declaration, StringComparison.Ordinal);
        Assert.Contains("converged behind the shared module-execution gate", risks, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "worker and polling paths still rely on partially separate control patterns",
            risks,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleGuideDoesNotAdvertiseUnsupportedIdentityUserDeletion()
    {
        var moduleGuide = RepositoryFiles.ReadAllText("docs/MODULE_GUIDE.md");

        Assert.DoesNotContain(
            "user administration (create, disable, enable, delete)",
            moduleGuide,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BaselineDeclarationTeachingPathIncludesBlog()
    {
        var declaration = RepositoryFiles.ReadAllText("docs/BASELINE_DECLARATION.md");

        var teachingPathIndex = declaration.IndexOf(
            "The recommended teaching path for contributors remains:",
            StringComparison.Ordinal);
        Assert.True(
            teachingPathIndex >= 0,
            "BASELINE_DECLARATION.md must keep the recommended teaching path section.");

        var teachingPathSegment = declaration[teachingPathIndex..];
        var nextHeadingIndex = teachingPathSegment.IndexOf("\n## ", StringComparison.Ordinal);
        if (nextHeadingIndex >= 0)
        {
            teachingPathSegment = teachingPathSegment[..nextHeadingIndex];
        }

        Assert.Contains("Blog/README.md", teachingPathSegment, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceModuleTeachingMapLinksResolve()
    {
        const string teachingMapPath = "docs/REFERENCE_MODULE_TEACHING_MAP.md";
        var markdown = RepositoryFiles.ReadAllText(teachingMapPath);
        var brokenLinks = new List<string>();

        foreach (Match match in Regex.Matches(markdown, @"\[[^\]]+\]\((?<target>[^)]+)\)", RegexOptions.CultureInvariant))
        {
            var target = match.Groups["target"].Value;
            if (string.IsNullOrWhiteSpace(target)
                || target.StartsWith("#", StringComparison.Ordinal)
                || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedTarget = target.Split('#', 2)[0];
            var fullPath = Path.GetFullPath(
                Path.Combine(
                    Path.GetDirectoryName(RepositoryFiles.PathFromRoot(teachingMapPath.Split('/')))!,
                    normalizedTarget));
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                brokenLinks.Add(target);
            }
        }

        Assert.True(
            brokenLinks.Count == 0,
            $"REFERENCE_MODULE_TEACHING_MAP.md contains broken relative links: {string.Join(", ", brokenLinks)}");
    }
}
