namespace Architecture.Tests;

public sealed class ProjectReferenceBoundaryTests
{
    [Fact]
    public void ApiHostReferencesOnlyModuleApiProjectsAndAllowedBuildingBlocks()
    {
        var expectedModuleApiProjects = RepositoryFiles.ReadProjectsUnder("src", "Modules")
            .Where(project => project.ProjectName.EndsWith(".Api", StringComparison.Ordinal))
            .Select(project => project.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        AssertProjectReferences(
            "src/ApiHost/ApiHost.csproj",
            new[]
            {
                "src/BuildingBlocks/Application/BuildingBlocks.Application.csproj",
                "src/BuildingBlocks/Infrastructure/BuildingBlocks.Infrastructure.csproj"
            }.Concat(expectedModuleApiProjects).ToArray());
    }

    [Fact]
    public void SharedBuildingBlocksProjectsUseTheGovernedReferenceShape()
    {
        AssertProjectReferences(
            "src/BuildingBlocks/Application/BuildingBlocks.Application.csproj",
            "src/BuildingBlocks/Domain/BuildingBlocks.Domain.csproj");
        AssertProjectReferences("src/BuildingBlocks/Domain/BuildingBlocks.Domain.csproj");
        AssertProjectReferences(
            "src/BuildingBlocks/Infrastructure/BuildingBlocks.Infrastructure.csproj",
            "src/BuildingBlocks/Application/BuildingBlocks.Application.csproj",
            "src/BuildingBlocks/Domain/BuildingBlocks.Domain.csproj");
        AssertProjectReferences(
            "src/BuildingBlocks/Testing/BuildingBlocks.Testing.csproj",
            "src/BuildingBlocks/Application/BuildingBlocks.Application.csproj",
            "src/BuildingBlocks/Domain/BuildingBlocks.Domain.csproj",
            "src/BuildingBlocks/Infrastructure/BuildingBlocks.Infrastructure.csproj");
    }

    [Fact]
    public void ModuleProjectsStayWithinTheirAllowedReferenceBoundaries()
    {
        foreach (var moduleName in RepositoryFiles.ReadModuleNames())
        {
            var apiProject = RepositoryFiles.ReadProject($"src/Modules/{moduleName}/{moduleName}.Api/{moduleName}.Api.csproj".Split('/'));
            var applicationProject = RepositoryFiles.ReadProject($"src/Modules/{moduleName}/{moduleName}.Application/{moduleName}.Application.csproj".Split('/'));
            var domainProject = RepositoryFiles.ReadProject($"src/Modules/{moduleName}/{moduleName}.Domain/{moduleName}.Domain.csproj".Split('/'));
            var infrastructureProject = RepositoryFiles.ReadProject($"src/Modules/{moduleName}/{moduleName}.Infrastructure/{moduleName}.Infrastructure.csproj".Split('/'));
            var publicContractsProject = RepositoryFiles.ReadProject($"src/Modules/{moduleName}/{moduleName}.PublicContracts/{moduleName}.PublicContracts.csproj".Split('/'));

            Assert.Equal(
                new[]
                {
                    $"src/BuildingBlocks/Application/BuildingBlocks.Application.csproj",
                    $"src/Modules/{moduleName}/{moduleName}.Application/{moduleName}.Application.csproj",
                    $"src/Modules/{moduleName}/{moduleName}.Infrastructure/{moduleName}.Infrastructure.csproj",
                    $"src/Modules/{moduleName}/{moduleName}.PublicContracts/{moduleName}.PublicContracts.csproj"
                },
                apiProject.ProjectReferences.OrderBy(path => path, StringComparer.Ordinal));

            AssertProjectUsesOnlyOwnModuleOrPublicContracts(
                applicationProject,
                moduleName,
                "src/BuildingBlocks/Application/BuildingBlocks.Application.csproj",
                $"src/Modules/{moduleName}/{moduleName}.Domain/{moduleName}.Domain.csproj",
                $"src/Modules/{moduleName}/{moduleName}.PublicContracts/{moduleName}.PublicContracts.csproj");

            Assert.Equal(
                new[] { "src/BuildingBlocks/Domain/BuildingBlocks.Domain.csproj" },
                domainProject.ProjectReferences.OrderBy(path => path, StringComparer.Ordinal));

            AssertProjectUsesOnlyOwnModuleOrPublicContracts(
                infrastructureProject,
                moduleName,
                "src/BuildingBlocks/Infrastructure/BuildingBlocks.Infrastructure.csproj",
                $"src/Modules/{moduleName}/{moduleName}.Application/{moduleName}.Application.csproj",
                $"src/Modules/{moduleName}/{moduleName}.Domain/{moduleName}.Domain.csproj",
                $"src/Modules/{moduleName}/{moduleName}.PublicContracts/{moduleName}.PublicContracts.csproj");

            Assert.All(publicContractsProject.ProjectReferences, reference =>
                Assert.Equal("src/BuildingBlocks/Domain/BuildingBlocks.Domain.csproj", reference));
        }
    }

    [Fact]
    public void DbMigratorReferencesModuleApiCompositionProjectsAndAllowedBuildingBlocks()
    {
        var expectedModuleApiProjects = RepositoryFiles.ReadProjectsUnder("src", "Modules")
            .Where(project => project.ProjectName.EndsWith(".Api", StringComparison.Ordinal))
            .Select(project => project.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        AssertProjectReferences(
            "src/Tools/DbMigrator/DbMigrator.csproj",
            new[]
            {
                "src/BuildingBlocks/Infrastructure/BuildingBlocks.Infrastructure.csproj"
            }.Concat(expectedModuleApiProjects).ToArray());
    }

    [Fact]
    public void CrossModuleReferencesTargetOnlyPublicContracts()
    {
        var moduleProjects = RepositoryFiles.ReadProjectsUnder("src", "Modules");

        foreach (var project in moduleProjects)
        {
            var currentModule = GetModuleName(project.RelativePath);

            foreach (var reference in project.ProjectReferences)
            {
                if (!reference.StartsWith("src/Modules/", StringComparison.Ordinal))
                {
                    continue;
                }

                var referencedModule = GetModuleName(reference);
                if (string.Equals(currentModule, referencedModule, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.EndsWith(".PublicContracts.csproj", reference, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TeachingModuleGraphIsDirectional()
    {
        // Item 5 invariant: simpler teaching modules never depend back on more advanced ones.
        // Platform and Identity are baseline platform subsystems and any teaching module may
        // consume their PublicContracts. Among the teaching modules, only Admin is allowed to
        // reach back into other teaching modules' PublicContracts (for projection examples
        // and shared-read demonstrations). SampleFeature, Blog, and KnowledgeBase must remain
        // standalone with respect to each other and Admin.
        var baselineModules = new HashSet<string>(StringComparer.Ordinal) { "Platform", "Identity" };
        var teachingModules = new HashSet<string>(StringComparer.Ordinal) { "SampleFeature", "Blog", "KnowledgeBase", "Admin" };
        var simplerTeachingModules = new HashSet<string>(StringComparer.Ordinal) { "SampleFeature", "Blog", "KnowledgeBase" };

        var moduleProjects = RepositoryFiles.ReadProjectsUnder("src", "Modules");

        foreach (var project in moduleProjects)
        {
            var currentModule = GetModuleName(project.RelativePath);

            foreach (var reference in project.ProjectReferences)
            {
                if (!reference.StartsWith("src/Modules/", StringComparison.Ordinal))
                {
                    continue;
                }

                var referencedModule = GetModuleName(reference);
                if (string.Equals(currentModule, referencedModule, StringComparison.Ordinal))
                {
                    continue;
                }

                if (baselineModules.Contains(referencedModule))
                {
                    continue;
                }

                Assert.True(
                    !simplerTeachingModules.Contains(currentModule) || !teachingModules.Contains(referencedModule),
                    $"Teaching module '{currentModule}' must not depend on '{referencedModule}'. " +
                    "Only Admin is allowed to reach backward into other teaching modules.");
            }
        }
    }

    [Fact]
    public void ProductionProjectsDoNotReferenceBuildingBlocksTesting()
    {
        var productionProjects = RepositoryFiles.ReadProjectsUnder("src");

        Assert.All(
            productionProjects,
            project => Assert.DoesNotContain(
                project.ProjectReferences,
                reference => string.Equals(
                    "src/BuildingBlocks/Testing/BuildingBlocks.Testing.csproj",
                    reference,
                    StringComparison.Ordinal)));
    }

    private static void AssertProjectReferences(string relativePath, params string[] expectedReferences)
    {
        var project = RepositoryFiles.ReadProject(relativePath.Split('/'));
        var orderedExpected = expectedReferences
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var orderedActual = project.ProjectReferences.OrderBy(path => path, StringComparer.Ordinal).ToArray();

        Assert.Equal(orderedExpected, orderedActual);
    }

    private static void AssertProjectUsesOnlyOwnModuleOrPublicContracts(
        ProjectFileModel project,
        string moduleName,
        params string[] requiredReferences)
    {
        Assert.All(project.ProjectReferences, reference =>
        {
            if (!reference.StartsWith("src/Modules/", StringComparison.Ordinal))
            {
                return;
            }

            var referencedModule = GetModuleName(reference);
            if (string.Equals(referencedModule, moduleName, StringComparison.Ordinal))
            {
                return;
            }

            Assert.EndsWith(".PublicContracts.csproj", reference, StringComparison.Ordinal);
        });

        var orderedExpected = requiredReferences
            .Concat(project.ProjectReferences.Where(reference =>
                reference.StartsWith("src/Modules/", StringComparison.Ordinal)
                && !string.Equals(GetModuleName(reference), moduleName, StringComparison.Ordinal)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(orderedExpected, project.ProjectReferences.OrderBy(path => path, StringComparer.Ordinal));
    }

    private static string GetModuleName(string relativePath)
    {
        var parts = relativePath.Split('/');
        return parts[2];
    }
}
