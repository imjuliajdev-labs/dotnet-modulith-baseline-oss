namespace Architecture.Tests;

public sealed class WorkflowConfigurationFreshnessTests
{
    private static readonly string[] ScannedRelativeFiles =
    [
        "docker-compose.yml"
    ];

    private static readonly (string Directory, string Pattern)[] ScannedDirectoryGlobs =
    [
        ("src/ApiHost", "appsettings*.json"),
        (".github/workflows", "*.yml")
    ];

    [Fact]
    public void CommittedWorkflowConfigDoesNotReintroducePermissionEraMachineKeys()
    {
        var offenders = new List<string>();

        foreach (var (relativePath, text) in EnumerateScannedFiles())
        {
            if (text.Contains("Modules__Identity__SeededMachine__Permissions__", StringComparison.Ordinal)
                || text.Contains("SeededMachine:Permissions", StringComparison.Ordinal))
            {
                offenders.Add(relativePath);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Committed workflow/config assets must not reintroduce removed permission-era seeded-machine keys. Offending files: "
                + string.Join(", ", offenders));
    }

    [Fact]
    public void DockerComposeDeclaresRoleBasedSeededMachineConfiguration()
    {
        var composeText = RepositoryFiles.ReadAllText("docker-compose.yml");

        Assert.Contains("Modules__Identity__SeededMachine__Roles__0", composeText, StringComparison.Ordinal);
    }

    [Fact]
    public void CommittedWorkflowConfigDoesNotReintroducePermissionSnapshotVersion()
    {
        var offenders = new List<string>();

        foreach (var (relativePath, text) in EnumerateScannedFiles())
        {
            if (text.Contains("permissionSnapshotVersion", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(relativePath);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Committed workflow/config assets must not reintroduce permissionSnapshotVersion. Offending files: "
                + string.Join(", ", offenders));
    }

    private static IEnumerable<(string RelativePath, string Text)> EnumerateScannedFiles()
    {
        foreach (var relativePath in ScannedRelativeFiles)
        {
            var fullPath = RepositoryFiles.PathFromRoot(relativePath.Split('/'));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            yield return (relativePath, File.ReadAllText(fullPath));
        }

        foreach (var (directory, pattern) in ScannedDirectoryGlobs)
        {
            var fullDirectory = RepositoryFiles.PathFromRoot(directory.Split('/'));
            if (!Directory.Exists(fullDirectory))
            {
                continue;
            }

            foreach (var fullPath in Directory.GetFiles(fullDirectory, pattern, SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = Path.GetRelativePath(RepositoryFiles.Root, fullPath).Replace('\\', '/');
                yield return (relativePath, File.ReadAllText(fullPath));
            }
        }
    }
}
