using System.Text.Json;

namespace Architecture.Tests;

/// <summary>
/// BP-005 growth budget. Each shared BuildingBlocks project is capped at a
/// declared file count and lines-of-code count by
/// <c>governance/buildingblocks-budget.json</c>. Trivially adding a file or a
/// few hundred lines to a BuildingBlocks project fails this test until the
/// budget is raised in the same commit, forcing a deliberate conversation
/// about whether the new code is genuinely shared-kernel work or should live
/// inside an owning module.
///
/// "Lines of code" excludes blank lines and lines that begin with <c>//</c>
/// after trimming whitespace, so the signal tracks meaningful growth. There
/// are no per-file exemptions: anything that lives under
/// <c>src/BuildingBlocks/{Project}/</c> counts toward that project's budget.
/// </summary>
public sealed class BuildingBlocksGrowthBudgetTests
{
    private const string BudgetRelativePath = "governance/buildingblocks-budget.json";

    [Fact]
    public void BudgetManifestExistsAndCoversEveryBuildingBlocksProject()
    {
        var budget = LoadBudget();
        var projects = DiscoverBuildingBlocksProjects();

        var missing = projects.Where(name => !budget.Projects.ContainsKey(name)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.True(
            missing.Length == 0,
            "BuildingBlocks projects missing from " + BudgetRelativePath + ": " + string.Join(", ", missing));

        var extra = budget.Projects.Keys.Where(name => !projects.Contains(name)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.True(
            extra.Length == 0,
            BudgetRelativePath + " declares budgets for projects that no longer exist: " + string.Join(", ", extra));
    }

    [Fact]
    public void EveryBuildingBlocksProjectStaysWithinItsDeclaredBudget()
    {
        var budget = LoadBudget();
        var projects = DiscoverBuildingBlocksProjects();
        var failures = new List<string>();

        foreach (var projectName in projects)
        {
            if (!budget.Projects.TryGetValue(projectName, out var entry))
            {
                continue;
            }

            var (actualFiles, actualLoc) = MeasureProject(projectName);

            if (actualFiles > entry.MaxFiles)
            {
                failures.Add(
                    $"{projectName} exceeded its file budget (actual {actualFiles} / budget {entry.MaxFiles}). " +
                    "Either move the new file into the module that owns it, or raise the file budget in " +
                    BudgetRelativePath + " with a commit message explaining why this is shared-kernel work.");
            }

            if (actualLoc > entry.MaxLinesOfCode)
            {
                failures.Add(
                    $"{projectName} exceeded its LOC budget (actual {actualLoc} / budget {entry.MaxLinesOfCode}). " +
                    "Either move the new code into the module that owns it, or raise the LOC budget in " +
                    BudgetRelativePath + " with a commit message explaining why this is shared-kernel work and not module work.");
            }
        }

        Assert.True(
            failures.Count == 0,
            "BP-005 BuildingBlocks growth budget violations: "
                + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static GrowthBudget LoadBudget()
    {
        var path = RepositoryFiles.PathFromRoot(BudgetRelativePath.Split('/'));
        Assert.True(File.Exists(path), "Missing budget manifest at " + BudgetRelativePath + ".");

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        var budget = JsonSerializer.Deserialize<GrowthBudget>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize " + BudgetRelativePath + ".");

        Assert.NotEmpty(budget.Projects);
        return budget;
    }

    private static IReadOnlyList<string> DiscoverBuildingBlocksProjects()
    {
        var root = RepositoryFiles.PathFromRoot("src", "BuildingBlocks");
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.GetDirectories(root)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Select(static name => "BuildingBlocks." + name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static (int Files, int LinesOfCode) MeasureProject(string projectName)
    {
        var simpleName = projectName["BuildingBlocks.".Length..];
        var directory = RepositoryFiles.PathFromRoot("src", "BuildingBlocks", simpleName);
        if (!Directory.Exists(directory))
        {
            return (0, 0);
        }

        var sourceFiles = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsBinOrObj(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var loc = 0;
        foreach (var path in sourceFiles)
        {
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var trimmed = rawLine.AsSpan().Trim();
                if (trimmed.IsEmpty)
                {
                    continue;
                }

                if (trimmed.StartsWith("//"))
                {
                    continue;
                }

                loc++;
            }
        }

        return (sourceFiles.Length, loc);
    }

    private static bool ContainsBinOrObj(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        return path.Contains($"{sep}bin{sep}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{sep}obj{sep}", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record GrowthBudget(Dictionary<string, BudgetEntry> Projects);

    private sealed record BudgetEntry(int MaxFiles, int MaxLinesOfCode);
}
