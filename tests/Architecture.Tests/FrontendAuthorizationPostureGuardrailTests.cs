namespace Architecture.Tests;

public sealed class FrontendAuthorizationPostureGuardrailTests
{
    private static readonly (string Pattern, string Description)[] BannedSourcePatterns =
    [
        ("requiredPermission", "feature routes must declare role requirements, not permission requirements"),
        ("session?.permissions", "browser sessions are role-based and must not expose permission arrays"),
        (".permissions.includes(", "frontend authorization must not inspect permission arrays"),
        ("/identity/users/{actorId}/permissions", "the removed identity permissions endpoint must not reappear in frontend source"),
        ("permissionSnapshotVersion", "session invalidation uses ASP.NET Core Identity security stamps rather than permission snapshots")
    ];

    [Fact]
    public void FrontendSourceDoesNotContainPermissionEraAuthorizationPatterns()
    {
        var sourceFiles = Directory.GetFiles(RepositoryFiles.PathFromRoot("web", "src"), "*.*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}generated{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(sourceFiles);

        var violations = new List<string>();

        foreach (var fullPath in sourceFiles)
        {
            var relativePath = Path.GetRelativePath(RepositoryFiles.Root, fullPath).Replace('\\', '/');
            var source = File.ReadAllText(fullPath);

            foreach (var (pattern, description) in BannedSourcePatterns)
            {
                if (source.Contains(pattern, StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}: '{pattern}' ({description})");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Frontend source reintroduced permission-era authorization patterns:\n" + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void FrontendRoutingUsesRoleBasedVisibilityContracts()
    {
        var featureRegistry = RepositoryFiles.ReadAllText("web/src/app/router/featureRegistry.tsx");
        var appRouter = RepositoryFiles.ReadAllText("web/src/app/router/AppRouter.tsx");

        Assert.Contains("requiredRoles?: string[]", featureRegistry, StringComparison.Ordinal);
        Assert.DoesNotContain("requiredPermission?: string", featureRegistry, StringComparison.Ordinal);
        Assert.Contains("const roles = new Set(session?.roles ?? []);", appRouter, StringComparison.Ordinal);
        Assert.DoesNotContain("session?.permissions", appRouter, StringComparison.Ordinal);
    }
}
