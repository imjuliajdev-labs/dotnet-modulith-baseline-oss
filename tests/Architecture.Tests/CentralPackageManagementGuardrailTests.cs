using System.Xml.Linq;

namespace Architecture.Tests;

public sealed class CentralPackageManagementGuardrailTests
{
    [Fact]
    public void NoProjectDeclaresALocalPackageVersionOrVersionOverride()
    {
        var projectFiles = RepositoryFiles.ReadProjectsUnder("src")
            .Concat(RepositoryFiles.ReadProjectsUnder("tests"))
            .ToArray();

        var violations = new List<string>();

        foreach (var project in projectFiles)
        {
            var fullPath = RepositoryFiles.PathFromRoot(project.RelativePath.Split('/'));
            var document = XDocument.Load(fullPath);

            var packageReferences = document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference");

            foreach (var packageReference in packageReferences)
            {
                var packageName = packageReference.Attribute("Include")?.Value ?? "<unknown>";

                if (packageReference.Attribute("Version") is not null)
                {
                    violations.Add($"{project.RelativePath}: PackageReference '{packageName}' declares a local Version attribute. Use Directory.Packages.props instead.");
                }

                if (packageReference.Attribute("VersionOverride") is not null)
                {
                    violations.Add($"{project.RelativePath}: PackageReference '{packageName}' uses VersionOverride. Central package management must not be bypassed.");
                }
            }
        }

        Assert.Empty(violations);
    }
}
