using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class DocumentationReferenceIntegrityTests
{
    private static readonly Regex MarkdownLinkPattern = new(
        @"\[[^\]]+\]\((?<target>[^)]+)\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex RuleIdPattern = new(
        @"\bBP-\d{3}\b",
        RegexOptions.CultureInvariant);

    [Fact]
    public void DurableMarkdownDocsHaveNoBrokenRelativeLinks()
    {
        var brokenLinks = new List<string>();

        foreach (var relativePath in EnumerateDurableMarkdownFiles())
        {
            var markdown = RepositoryFiles.ReadAllText(relativePath);
            var sourceDirectory = Path.GetDirectoryName(RepositoryFiles.PathFromRoot(relativePath.Split('/')))
                ?? throw new InvalidOperationException($"Could not resolve the directory for '{relativePath}'.");

            foreach (Match match in MarkdownLinkPattern.Matches(markdown))
            {
                var target = match.Groups["target"].Value;
                if (string.IsNullOrWhiteSpace(target)
                    || target.StartsWith("#", StringComparison.Ordinal)
                    || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var normalizedTarget = target.Split('#', 2)[0];
                var fullPath = Path.GetFullPath(Path.Combine(sourceDirectory, normalizedTarget));
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    brokenLinks.Add($"{relativePath} -> {target}");
                }
            }
        }

        Assert.True(
            brokenLinks.Count == 0,
            "Durable markdown docs contain broken relative links: " + string.Join(", ", brokenLinks));
    }

    [Fact]
    public void DurableMarkdownDocsOnlyReferenceKnownRuleIds()
    {
        var knownRuleIds = RepositoryFiles.ReadRuleCatalog()
            .Select(static entry => entry.RuleId)
            .ToHashSet(StringComparer.Ordinal);

        var unknownReferences = new List<string>();

        foreach (var relativePath in EnumerateDurableMarkdownFiles())
        {
            var markdown = RepositoryFiles.ReadAllText(relativePath);
            foreach (Match match in RuleIdPattern.Matches(markdown))
            {
                var ruleId = match.Value;
                if (!knownRuleIds.Contains(ruleId))
                {
                    unknownReferences.Add($"{relativePath} -> {ruleId}");
                }
            }
        }

        Assert.True(
            unknownReferences.Count == 0,
            "Durable markdown docs reference unknown rule ids: " + string.Join(", ", unknownReferences));
    }

    [Fact]
    public void DeploymentDocKeepsHealthProbePathsAlignedWithApiHostComposition()
    {
        var deploymentDoc = RepositoryFiles.ReadAllText("docs/DEPLOYMENT.md");
        var apiHostComposition = RepositoryFiles.ReadAllText("src/ApiHost/ApiHostComposition.cs");

        Assert.Contains("app.MapHealthChecks(\"/health\")", apiHostComposition, StringComparison.Ordinal);
        Assert.Contains("app.MapHealthChecks(\"/ready\"", apiHostComposition, StringComparison.Ordinal);

        Assert.Contains("`GET /health`", deploymentDoc, StringComparison.Ordinal);
        Assert.Contains("`GET /ready`", deploymentDoc, StringComparison.Ordinal);
        Assert.DoesNotContain("`GET /health/live`", deploymentDoc, StringComparison.Ordinal);
        Assert.DoesNotContain("`GET /health/ready`", deploymentDoc, StringComparison.Ordinal);
        Assert.DoesNotContain("`GET /health/startup`", deploymentDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentDocDescribesThePostgresBackedDataProtectionDefault()
    {
        var deploymentDoc = RepositoryFiles.ReadAllText("docs/DEPLOYMENT.md");

        Assert.Contains("PostgreSQL-backed", deploymentDoc, StringComparison.Ordinal);
        Assert.DoesNotContain("you must override it with one of", deploymentDoc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationRollbackDocsUseDurableRollbackRunbooks()
    {
        var migrationRollbackDoc = RepositoryFiles.ReadAllText("docs/MIGRATION_ROLLBACK.md");
        var rollbackOperationsIndex = RepositoryFiles.ReadAllText("docs/operations/rollback/README.md");
        var migrationSource = RepositoryFiles.ReadAllText(
            "src/Modules/Blog/Blog.Infrastructure/Persistence/Migrations/20260413004835_ContractBlogPostLegacyPascalCaseColumns.cs");

        Assert.DoesNotContain("docs/_local/", migrationRollbackDoc, StringComparison.Ordinal);
        Assert.Contains("operations/rollback/blog-contract-legacy-pascalcase-columns.md", migrationRollbackDoc, StringComparison.Ordinal);
        Assert.Contains("blog-contract-legacy-pascalcase-columns.md", rollbackOperationsIndex, StringComparison.Ordinal);
        Assert.Contains("docs/operations/rollback/blog-contract-legacy-pascalcase-columns.md", migrationSource, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> EnumerateDurableMarkdownFiles()
    {
        var docsRoot = RepositoryFiles.PathFromRoot("docs");
        var markdownFiles = Directory.GetFiles(docsRoot, "*.md", SearchOption.AllDirectories)
            .Select(static path => Path.GetRelativePath(RepositoryFiles.Root, path).Replace('\\', '/'))
            .Where(static relativePath => !relativePath.StartsWith("docs/_local/", StringComparison.Ordinal))
            .OrderBy(static relativePath => relativePath, StringComparer.Ordinal)
            .ToList();

        markdownFiles.Add("README.md");
        markdownFiles.Add("AGENTS.md");

        return markdownFiles
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static relativePath => relativePath, StringComparer.Ordinal)
            .ToArray();
    }
}
