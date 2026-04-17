using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class PublicListEndpointPaginationGuardrailTests
{
    // A minimal-API endpoint fluent chain starts at a `Map{Verb}(` call and runs until the next
    // statement-terminating `;` at brace depth zero. We extract each chain as text and reason
    // about its `.Produces<T>(...)` and `.WithNonCursorListEndpoint(...)` calls without booting
    // an ASP.NET host — booting the host for an architecture gate would make the test slow and
    // reintroduce composition coupling the baseline explicitly forbids.

    private static readonly Regex MapGetInvocation = new(
        @"\bMapGet\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex ProducesResponse = new(
        @"\.Produces<\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*>",
        RegexOptions.Compiled);

    private static readonly Regex WithName = new(
        @"\.WithName\(\s*""(?<name>[^""]+)""\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex WithNonCursorListEndpointCall = new(
        @"\.WithNonCursorListEndpoint\(\s*""(?<reason>[^""]*)""\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void NonCursorListEndpointsMustCarryDocumentedReason()
    {
        var violations = new List<string>();

        foreach (var chain in EnumerateMapGetChains())
        {
            var responseType = FirstSuccessResponseType(chain.Body);
            if (responseType is null)
            {
                continue;
            }

            if (IsCursorPagedResponseType(responseType))
            {
                continue;
            }

            if (!IsListShapedResponseType(responseType))
            {
                continue;
            }

            var exemptionMatch = WithNonCursorListEndpointCall.Match(chain.Body);
            if (exemptionMatch.Success && !string.IsNullOrWhiteSpace(exemptionMatch.Groups["reason"].Value))
            {
                continue;
            }

            var endpointName = WithName.Match(chain.Body) is { Success: true } nameMatch
                ? nameMatch.Groups["name"].Value
                : "<unnamed>";
            violations.Add(
                $"{chain.File}: MapGet endpoint '{endpointName}' returns list-shaped '{responseType}' "
                + "which does not carry NextCursor and is not annotated with .WithNonCursorListEndpoint(\"<reason>\"). "
                + "Either convert the endpoint to cursor pagination (CursorPagedResult<T>) or, if the list is bounded "
                + "by design, call .WithNonCursorListEndpoint(\"<explicit reason>\") to record the exemption.");
        }

        Assert.True(
            violations.Count == 0,
            "BP-032 violation(s):" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ExemptionCallsMustProvideNonEmptyReason()
    {
        var violations = new List<string>();

        foreach (var filePath in EnumerateApiSourceFiles())
        {
            var text = RepositoryFiles.ReadAllText(filePath);
            foreach (Match match in WithNonCursorListEndpointCall.Matches(text))
            {
                if (string.IsNullOrWhiteSpace(match.Groups["reason"].Value))
                {
                    violations.Add($"{filePath}: .WithNonCursorListEndpoint(...) missing a non-empty reason.");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "BP-032 exemption(s) missing reason:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ExemptionHelperLivesInBuildingBlocksHttp()
    {
        // The exemption helper is a single canonical shared-surface entry. If someone copies it
        // into a module (name-based shim), this test fires.
        var modulesRoot = RepositoryFiles.PathFromRoot("src", "Modules");
        var offenders = Directory
            .GetFiles(modulesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("class NonCursorListEndpointConventionBuilder", StringComparison.Ordinal)
                    || text.Contains("record NonCursorListEndpointMetadata", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "The non-cursor-list-endpoint exemption helper must live only in BuildingBlocks.Infrastructure.Http. "
            + "Offenders:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<MapGetChain> EnumerateMapGetChains()
    {
        foreach (var filePath in EnumerateApiSourceFiles())
        {
            var text = RepositoryFiles.ReadAllText(filePath);
            foreach (Match match in MapGetInvocation.Matches(text))
            {
                var chainBody = ExtractChainBody(text, match.Index);
                if (chainBody is null)
                {
                    continue;
                }

                yield return new MapGetChain(filePath, chainBody);
            }
        }
    }

    private static IEnumerable<string> EnumerateApiSourceFiles()
    {
        return RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains(".Api/", StringComparison.Ordinal));
    }

    private static string? ExtractChainBody(string text, int mapGetStart)
    {
        // Starting at the `MapGet(` token, walk forward tracking paren/brace depth and string
        // escapes. The chain terminates at the first `;` encountered at depth zero outside a
        // string. Returning the substring from the MapGet start to the terminator lets the rest
        // of the test reason about the full fluent chain as a single flat body.
        var parenDepth = 0;
        var braceDepth = 0;
        var inString = false;
        var inVerbatim = false;
        var inChar = false;

        for (var index = mapGetStart; index < text.Length; index++)
        {
            var current = text[index];

            if (inString)
            {
                if (!inVerbatim && current == '\\' && index + 1 < text.Length)
                {
                    index++;
                    continue;
                }
                if (current == '"')
                {
                    if (inVerbatim && index + 1 < text.Length && text[index + 1] == '"')
                    {
                        index++;
                        continue;
                    }
                    inString = false;
                    inVerbatim = false;
                }
                continue;
            }

            if (inChar)
            {
                if (current == '\\' && index + 1 < text.Length)
                {
                    index++;
                    continue;
                }
                if (current == '\'')
                {
                    inChar = false;
                }
                continue;
            }

            switch (current)
            {
                case '"':
                    inString = true;
                    inVerbatim = index > 0 && text[index - 1] == '@';
                    break;
                case '\'':
                    inChar = true;
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    braceDepth--;
                    break;
                case ';' when parenDepth == 0 && braceDepth == 0:
                    return text.Substring(mapGetStart, index - mapGetStart + 1);
            }
        }

        return null;
    }

    private static string? FirstSuccessResponseType(string chainBody)
    {
        foreach (Match match in ProducesResponse.Matches(chainBody))
        {
            var typeName = match.Groups["type"].Value;
            if (!typeName.StartsWith("Problem", StringComparison.Ordinal))
            {
                return typeName;
            }
        }

        return null;
    }

    private static readonly Regex ListResponseNamePattern = new(
        @"^[A-Za-z0-9_]+ListResponse(?:V\d+)?$",
        RegexOptions.Compiled);

    private static bool IsCursorPagedResponseType(string responseTypeName)
    {
        // A response is cursor-paged if its CLR shape carries a NextCursor/Cursor string. We
        // resolve the type across loaded {Module}.Api assemblies rather than trusting the name,
        // because the name-based check would admit anything containing "Paged" in its string.
        return ResponseTypeHasNextCursorProperty(responseTypeName);
    }

    private static bool IsListShapedResponseType(string responseTypeName)
    {
        // The convention in this repo is that every collection-primary response carries the
        // `ListResponse` suffix (optionally followed by a `V\d+` contract version). We use the
        // name as the authoritative classifier because a structural "has any IEnumerable
        // property" check would also flag scalar envelopes (e.g. BlogPostResponse has a
        // ShareTargets array) as lists. The runbook asks for a typed gate, not a heuristic.
        return ListResponseNamePattern.IsMatch(responseTypeName);
    }

    private static bool ResponseTypeHasNextCursorProperty(string responseTypeName)
    {
        var type = ResolveApiResponseType(responseTypeName);
        if (type is null)
        {
            return false;
        }

        foreach (var property in type.GetProperties())
        {
            if (property.PropertyType == typeof(string)
                && (property.Name.Equals("NextCursor", StringComparison.Ordinal)
                    || property.Name.Equals("Cursor", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static Type? ResolveApiResponseType(string simpleTypeName)
    {
        foreach (var assembly in RepositoryFiles.ReadModuleApiAssemblies())
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.Name.Equals(simpleTypeName, StringComparison.Ordinal))
                {
                    return type;
                }
            }
        }

        return null;
    }

    private sealed record MapGetChain(string File, string Body);
}
