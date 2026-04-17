using System.Reflection;
using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class PublicContractsGuardrailTests
{
    private static readonly Regex TypeDeclarationPattern = new(
        @"\b(?:record|class|interface|enum)\s+([A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CrossModuleQueryUsagePattern = new(
        @"using\s+(?<provider>[A-Z][A-Za-z0-9]+)\.PublicContracts\.Queries;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void PublicContractsStayWithinGovernedFoldersAndNamespaces()
    {
        var contractFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains(".PublicContracts/", StringComparison.Ordinal))
            .Where(path => !path.EndsWith("AssemblyMarker.cs", StringComparison.Ordinal))
            .ToArray();

        Assert.All(contractFiles, relativePath =>
        {
            var source = RepositoryFiles.ReadAllText(relativePath);
            var declaredTypes = TypeDeclarationPattern.Matches(source)
                .Select(static match => match.Groups[1].Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.NotEmpty(declaredTypes);

            if (relativePath.Contains(".PublicContracts/Events/", StringComparison.Ordinal))
            {
                Assert.Contains(".PublicContracts.Events", source, StringComparison.Ordinal);
                return;
            }

            if (relativePath.Contains(".PublicContracts/Queries/", StringComparison.Ordinal))
            {
                Assert.Contains(".PublicContracts.Queries", source, StringComparison.Ordinal);
                return;
            }

            if (relativePath.Contains(".PublicContracts/Ids/", StringComparison.Ordinal)
                || relativePath.Contains(".PublicContracts/Identifiers/", StringComparison.Ordinal))
            {
                Assert.Contains(".PublicContracts.", source, StringComparison.Ordinal);
                return;
            }

            if (relativePath.Contains(".PublicContracts/Enums/", StringComparison.Ordinal))
            {
                Assert.Contains(".PublicContracts.Enums", source, StringComparison.Ordinal);
                return;
            }

            Assert.Fail(
                $"Public contract file '{relativePath}' must live under Events, Queries, Ids, Identifiers, or Enums.");
        });
    }

    [Fact]
    public void IntegrationEventContractsUseVersionedNames()
    {
        var contractFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains(".PublicContracts/Events/", StringComparison.Ordinal))
            .Where(path => !path.EndsWith("AssemblyMarker.cs", StringComparison.Ordinal))
            .ToArray();

        Assert.All(contractFiles, relativePath =>
        {
            var source = RepositoryFiles.ReadAllText(relativePath);
            var declaredTypes = TypeDeclarationPattern.Matches(source)
                .Select(static match => match.Groups[1].Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.NotEmpty(declaredTypes);
            Assert.All(declaredTypes, typeName => Assert.Matches(@".*V\d+$", typeName));
        });
    }

    [Fact]
    public void PublicContractsDoNotContainTransportOrHandlerInternalTypes()
    {
        var contractFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains(".PublicContracts/", StringComparison.Ordinal))
            .Where(path => !path.EndsWith("AssemblyMarker.cs", StringComparison.Ordinal))
            .ToArray();

        var forbiddenTokens = new[]
        {
            "ICommand",
            "IQuery<",
            "IQueryHandler",
            "ICommandHandler",
            "EndpointFilter",
            "IEndpointRouteBuilder",
            "HttpContext",
            "Results.",
            "DbContext",
            ".Api.Contracts",
            "using Microsoft.AspNetCore",
            "using Microsoft.EntityFrameworkCore"
        };

        var forbiddenTypeNamePattern = new Regex(
            @"\b(?:record|class|interface|enum)\s+[A-Za-z0-9_]*(?:Command|Handler|Endpoint|Dto)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        Assert.All(contractFiles, relativePath =>
        {
            var source = RepositoryFiles.ReadAllText(relativePath);

            Assert.DoesNotMatch(forbiddenTypeNamePattern, source);
            foreach (var token in forbiddenTokens)
            {
                Assert.DoesNotContain(token, source, StringComparison.Ordinal);
            }
        });
    }

    [Fact]
    public void CrossModuleSharedQueryContractsStayOutOfApiAndDomainLayers()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => !path.Contains(".PublicContracts/", StringComparison.Ordinal))
            .ToArray();

        foreach (var relativePath in sourceFiles)
        {
            var source = RepositoryFiles.ReadAllText(relativePath);
            var currentModule = relativePath.Split('/')[2];
            var isApi = relativePath.Contains($"/{currentModule}.Api/", StringComparison.Ordinal);
            var isDomain = relativePath.Contains($"/{currentModule}.Domain/", StringComparison.Ordinal);
            var isAllowedConsumerLayer = relativePath.Contains($"/{currentModule}.Application/", StringComparison.Ordinal)
                || relativePath.Contains($"/{currentModule}.Infrastructure/", StringComparison.Ordinal);

            foreach (Match match in CrossModuleQueryUsagePattern.Matches(source))
            {
                var providerModule = match.Groups["provider"].Value;
                if (string.Equals(providerModule, currentModule, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.False(
                    isApi || isDomain,
                    $"Cross-module shared query contracts must stay out of API and Domain. Found '{providerModule}.PublicContracts.Queries' in '{relativePath}'.");
                Assert.True(
                    isAllowedConsumerLayer,
                    $"Cross-module shared query contracts must be consumed from Application or Infrastructure. Found '{providerModule}.PublicContracts.Queries' in '{relativePath}'.");
            }
        }
    }

    [Fact]
    public void QueryContractNamespacesMustContainAtLeastOneServiceInterface()
    {
        var violations = new List<string>();

        foreach (var assembly in RepositoryFiles.ReadModulePublicContractsAssemblies())
        {
            var queryTypes = assembly.GetExportedTypes()
                .Where(static type => type.Namespace is { } ns && ns.EndsWith(".PublicContracts.Queries", StringComparison.Ordinal))
                .ToArray();

            if (queryTypes.Length == 0)
            {
                continue;
            }

            var hasServiceInterface = queryTypes.Any(static type => type.IsInterface);
            if (!hasServiceInterface)
            {
                violations.Add(
                    $"{assembly.GetName().Name}: Queries namespace contains types but no query service interface. " +
                    "Every PublicContracts Queries namespace must include at least one service interface that governs the read surface.");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Query contract namespaces without a governing service interface:\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void QueryReadModelsAndResponsesMustCohabitWithAServiceInterface()
    {
        var violations = new List<string>();

        foreach (var assembly in RepositoryFiles.ReadModulePublicContractsAssemblies())
        {
            var queryNamespaces = assembly.GetExportedTypes()
                .Where(static type => type.Namespace is { } ns && ns.EndsWith(".PublicContracts.Queries", StringComparison.Ordinal))
                .GroupBy(static type => type.Namespace!, StringComparer.Ordinal);

            foreach (var group in queryNamespaces)
            {
                var types = group.ToArray();
                var interfaces = types.Where(static type => type.IsInterface).ToArray();

                if (interfaces.Length == 0)
                {
                    continue;
                }

                var referencedTypes = new HashSet<Type>();
                foreach (var iface in interfaces)
                {
                    foreach (var method in iface.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        CollectReferencedTypes(method.ReturnType, referencedTypes);
                        foreach (var parameter in method.GetParameters())
                        {
                            CollectReferencedTypes(parameter.ParameterType, referencedTypes);
                        }
                    }
                }

                bool changed;
                do
                {
                    changed = false;
                    foreach (var candidate in types.Where(t => !t.IsInterface && !referencedTypes.Contains(t)))
                    {
                        if (IsCompanionType(candidate, referencedTypes))
                        {
                            referencedTypes.Add(candidate);
                            changed = true;
                        }
                    }
                } while (changed);

                foreach (var type in types)
                {
                    if (type.IsInterface)
                    {
                        continue;
                    }

                    if (!referencedTypes.Contains(type))
                    {
                        violations.Add(
                            $"{type.FullName} is not referenced by any query service interface in its namespace. " +
                            "Non-interface types in PublicContracts Queries must be reachable from a query service interface or compose reachable types.");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Orphaned query contract types not referenced by a service interface:\n{string.Join("\n", violations)}");
    }

    private static void CollectReferencedTypes(Type type, HashSet<Type> collected)
    {
        if (type.Namespace is { } ns && ns.EndsWith(".PublicContracts.Queries", StringComparison.Ordinal))
        {
            collected.Add(type);
        }

        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                CollectReferencedTypes(arg, collected);
            }
        }
    }

    private static bool IsCompanionType(Type candidate, HashSet<Type> acceptedTypes)
    {
        var referencedQueryTypes = new HashSet<Type>();

        foreach (var constructor in candidate.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                CollectReferencedTypes(parameter.ParameterType, referencedQueryTypes);
            }
        }

        foreach (var property in candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            CollectReferencedTypes(property.PropertyType, referencedQueryTypes);
        }

        referencedQueryTypes.Remove(candidate);
        return referencedQueryTypes.Count > 0 && referencedQueryTypes.All(acceptedTypes.Contains);
    }
}
