namespace Architecture.Tests.Support;

/// <summary>
/// Shared inventory logic behind both the <c>Report-ModuleDependencies.ps1</c> script and
/// <c>ModuleDependencyReportTests</c>. It walks every module csproj under <c>src/Modules</c>,
/// collects distinct cross-module <c>*.PublicContracts.csproj</c> references, and returns a
/// deterministic per-module row set sorted by module name.
///
/// The cross-module reference cap mirrors <see cref="CrossModuleReferenceCapGuardrailTests"/>;
/// it is duplicated here intentionally so the report can be run without loading the xUnit
/// runner, and is kept in sync by the canary test in <c>ModuleDependencyReportTests</c>.
/// </summary>
internal static class ModuleDependencyReportInventory
{
    public const int CrossModulePublicContractsReferenceCap = 3;

    public static IReadOnlyList<ModuleDependencyReportRow> Build()
    {
        var moduleProjects = RepositoryFiles.ReadProjectsUnder("src", "Modules");
        var moduleNames = RepositoryFiles.ReadModuleNames();
        var rows = new List<ModuleDependencyReportRow>(moduleNames.Count);

        foreach (var moduleName in moduleNames)
        {
            var referencedModules = moduleProjects
                .Where(project => GetModuleName(project.RelativePath) == moduleName)
                .SelectMany(project => project.ProjectReferences)
                .Where(reference => reference.StartsWith("src/Modules/", StringComparison.Ordinal))
                .Where(reference => reference.EndsWith(".PublicContracts.csproj", StringComparison.Ordinal))
                .Where(reference => !string.Equals(GetModuleName(reference), moduleName, StringComparison.Ordinal))
                .Select(GetModuleName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            rows.Add(new ModuleDependencyReportRow(
                moduleName,
                referencedModules.Length,
                CrossModulePublicContractsReferenceCap - referencedModules.Length,
                referencedModules));
        }

        return rows
            .OrderBy(static row => row.Module, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetModuleName(string relativePath)
    {
        var parts = relativePath.Split('/');
        return parts[2];
    }
}

internal sealed record ModuleDependencyReportRow(
    string Module,
    int Refs,
    int Headroom,
    IReadOnlyList<string> References);
