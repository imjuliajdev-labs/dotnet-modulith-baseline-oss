using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class MigrationSafetyGuardrailTests
{
    private static readonly Regex _migrationAttributePattern = new(
        "\\[Migration\\(\"(?<id>[^\"]+)\"\\)\\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void MigrationFilesMatchEffectiveMigrationIdsAndStayMonotonicWithinEachFolder()
    {
        var migrations = EnumerateMigrations().ToArray();
        var violations = new List<string>();

        foreach (var migration in migrations)
        {
            if (!string.Equals(migration.FileBaseName, migration.MigrationId, StringComparison.Ordinal))
            {
                violations.Add(
                    $"{migration.RelativePath}: file name '{migration.FileBaseName}' must match the effective migration id '{migration.MigrationId}'.");
            }
        }

        foreach (var group in migrations.GroupBy(static migration => migration.DirectoryPath))
        {
            var fileOrder = group
                .OrderBy(static migration => migration.FileBaseName, StringComparer.Ordinal)
                .Select(static migration => migration.RelativePath)
                .ToArray();

            var idOrder = group
                .OrderBy(static migration => migration.MigrationId, StringComparer.Ordinal)
                .Select(static migration => migration.RelativePath)
                .ToArray();

            if (!fileOrder.SequenceEqual(idOrder, StringComparer.Ordinal))
            {
                violations.Add(
                    $"{group.Key}: migration file order does not match effective migration id order. File order: {string.Join(", ", fileOrder)}. Effective order: {string.Join(", ", idOrder)}.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void MigrationUpMethodsAvoidDestructiveOperationsDuringCompatibilityWindows()
    {
        var violations = new List<string>();
        var baselineReshapeAllowlist = ReadBaselineReshapeAllowlist();

        foreach (var migration in EnumerateMigrations())
        {
            if (baselineReshapeAllowlist.Contains(migration.FileBaseName))
            {
                continue;
            }

            var upBody = migration.UpBody;
            var destructiveTokens = new[]
            {
                "migrationBuilder.DropColumn(",
                "migrationBuilder.DropTable(",
                "migrationBuilder.DropForeignKey(",
                "migrationBuilder.DropPrimaryKey(",
                "migrationBuilder.RenameColumn(",
                "migrationBuilder.RenameTable(",
                "migrationBuilder.RenameIndex("
            };

            foreach (var token in destructiveTokens)
            {
                if (upBody.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{migration.RelativePath}: Up() contains destructive operation '{token}'. Use expand-and-contract sequencing instead.");
                }
            }

            var destructiveSqlTokens = new[]
            {
                "DROP TABLE",
                "DROP COLUMN",
                "RENAME TABLE",
                "RENAME COLUMN"
            };

            foreach (var token in destructiveSqlTokens)
            {
                if (upBody.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{migration.RelativePath}: Up() contains destructive SQL '{token}'. Use expand-and-contract sequencing instead.");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static HashSet<string> ReadBaselineReshapeAllowlist()
    {
        var allowlistPath = RepositoryFiles.PathFromRoot("tests", "Architecture.Tests", "Approved", "BaselineReshapeMigrations.txt");
        if (!File.Exists(allowlistPath))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return File.ReadAllLines(allowlistPath)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<MigrationFile> EnumerateMigrations()
    {
        return RepositoryFiles.ReadCSharpFilesUnder("src")
            .Where(static path => path.Contains("/Migrations/", StringComparison.Ordinal))
            .Where(static path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(static path => !path.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .Select(static relativePath =>
            {
                var source = RepositoryFiles.ReadAllText(relativePath);
                var directoryPath = Path.GetDirectoryName(relativePath)?.Replace('\\', '/')
                    ?? throw new InvalidOperationException($"Could not resolve the directory for migration '{relativePath}'.");

                return new MigrationFile(
                    relativePath,
                    directoryPath,
                    Path.GetFileNameWithoutExtension(relativePath),
                    ReadEffectiveMigrationId(relativePath, source),
                    ExtractUpBody(relativePath, source));
            })
            .OrderBy(static migration => migration.RelativePath, StringComparer.Ordinal);
    }

    private static string ReadEffectiveMigrationId(string relativePath, string source)
    {
        var match = _migrationAttributePattern.Match(source);
        if (match.Success)
        {
            return match.Groups["id"].Value;
        }

        var designerPath = relativePath[..^3] + ".Designer.cs";
        var designerFullPath = RepositoryFiles.PathFromRoot(designerPath.Split('/'));
        if (!File.Exists(designerFullPath))
        {
            throw new InvalidOperationException($"Migration '{relativePath}' did not declare a [Migration] attribute and has no designer file.");
        }

        var designerSource = RepositoryFiles.ReadAllText(designerPath);
        match = _migrationAttributePattern.Match(designerSource);

        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not read the effective migration id for '{relativePath}'.");
        }

        return match.Groups["id"].Value;
    }

    private static string ExtractUpBody(string relativePath, string source)
    {
        var upIndex = source.IndexOf("protected override void Up", StringComparison.Ordinal);
        var downIndex = source.IndexOf("protected override void Down", StringComparison.Ordinal);

        if (upIndex < 0 || downIndex < 0 || downIndex <= upIndex)
        {
            throw new InvalidOperationException($"Could not isolate the Up() body for migration '{relativePath}'.");
        }

        return source.Substring(upIndex, downIndex - upIndex);
    }

    private sealed record MigrationFile(
        string RelativePath,
        string DirectoryPath,
        string FileBaseName,
        string MigrationId,
        string UpBody);
}
