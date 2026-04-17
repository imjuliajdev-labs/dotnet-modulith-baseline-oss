using BuildingBlocks.Application.Authorization;
using Identity.Application.Administration;
using Identity.Application.Authorization;
using Identity.Infrastructure.Authentication;
using NodaTime;
using Platform.Api;
using Platform.Application.Authorization;
using Platform.Application.Auditing;
using Platform.Application.Health;
using Platform.Application.Bootstrap;
using Platform.Application.ModuleState;
using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class ApiEdgeGuardrailTests
{
    [Fact]
    public void SourceSkeletonDoesNotUseControllers()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src");
        var forbiddenTokens = new[]
        {
            "ControllerBase",
            "[ApiController]",
            "AddControllers(",
            "MapControllers(",
            "using Microsoft.AspNetCore.Mvc"
        };

        foreach (var relativePath in sourceFiles)
        {
            var text = RepositoryFiles.ReadAllText(relativePath);
            foreach (var token in forbiddenTokens)
            {
                Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ApiSourceSkeletonDoesNotUseEfCoreTypesDirectly()
    {
        var apiSourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains(".Api/", StringComparison.Ordinal))
            .ToArray();

        var forbiddenTokens = new[]
        {
            "DbContext",
            "EntityTypeBuilder",
            "using Microsoft.EntityFrameworkCore"
        };

        foreach (var relativePath in apiSourceFiles)
        {
            var text = RepositoryFiles.ReadAllText(relativePath);
            foreach (var token in forbiddenTokens)
            {
                Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void PlatformOperationalRequestsRequireExplicitAdminRole()
    {
        var requests = new IAuthorizeRequest[]
        {
            new EnableModuleCommand("reports"),
            new DisableModuleCommand("reports"),
            new GetPlatformAuditEventsQuery(),
            new GetOperationalHealthSummaryQuery()
        };

        Assert.All(requests, request =>
            Assert.Contains(
                request.AuthorizationRequirements,
                requirement => requirement.AllowedRoles.Contains(IdentityRoles.Admin)));
    }

    [Fact]
    public void PlatformOperationalMutationEndpointsRequireExplicitRateLimiting()
    {
        var platformModuleSource = RepositoryFiles.ReadAllText("src/Modules/Platform/Platform.Api/PlatformModule.cs");

        Assert.Contains("RequireRateLimiting(PlatformEndpointPolicies.ModuleStateMutations)", platformModuleSource, StringComparison.Ordinal);
        Assert.Contains("\"/{moduleKey}/enable\"", platformModuleSource, StringComparison.Ordinal);
        Assert.Contains("\"/{moduleKey}/disable\"", platformModuleSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DisableableModuleRouteGroupsUseSharedModuleStateHttpGateAndServiceUnavailableMetadata()
    {
        var compositionSource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Modules/ApiModuleComposition.cs");

        Assert.Contains("if (module.Descriptor.CanBeDisabled)", compositionSource, StringComparison.Ordinal);
        Assert.Contains("moduleGroup.AddEndpointFilter(new ModuleStateEndpointFilter(module.Key));", compositionSource, StringComparison.Ordinal);
        Assert.Contains("moduleGroup.ProducesProblem(StatusCodes.Status503ServiceUnavailable);", compositionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformOperationalMutationsRequireRecentAuthentication()
    {
        IRequireRecentAuthentication[] requests =
        [
            new EnableModuleCommand("reports"),
            new DisableModuleCommand("reports")
        ];

        Assert.All(requests, request => Assert.Equal(Duration.FromMinutes(5), request.RecentAuthenticationWindow));
    }

    [Fact]
    public void ModuleApiEndpointsRouteUseCasesThroughTheDispatcher()
    {
        var apiSourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains(".Api/", StringComparison.Ordinal))
            .ToArray();

        foreach (var relativePath in apiSourceFiles)
        {
            var text = RepositoryFiles.ReadAllText(relativePath);
            foreach (var endpointRegistration in ReadEndpointRegistrations(text))
            {
                if (IsAllowlistedInfrastructureEndpoint(relativePath, endpointRegistration))
                {
                    continue;
                }

                Assert.Contains("IDispatcher dispatcher", endpointRegistration, StringComparison.Ordinal);
                Assert.True(
                    RoutesThroughDispatcher(endpointRegistration),
                    $"Expected {DescribeEndpoint(relativePath, endpointRegistration)} to invoke dispatcher.Send(...) or dispatcher.Query(...).\n{endpointRegistration}");
            }
        }
    }

    [Fact]
    public void ModuleApiSourceDoesNotPerformEndpointLevelAntiforgeryValidation()
    {
        var apiSourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains(".Api/", StringComparison.Ordinal))
            .ToArray();

        foreach (var relativePath in apiSourceFiles)
        {
            var text = RepositoryFiles.ReadAllText(relativePath);
            Assert.DoesNotContain("ValidateRequestAsync(", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ApiHostComposesCentralBrowserMutationAntiforgeryBeforeAuthorization()
    {
        var composition = RepositoryFiles.ReadAllText("src/ApiHost/ApiHostComposition.cs");
        var authenticationIndex = composition.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var antiforgeryIndex = composition.IndexOf("app.UseBrowserMutationAntiforgery();", StringComparison.Ordinal);
        var authorizationIndex = composition.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);

        Assert.True(authenticationIndex >= 0, "Expected ApiHost to enable authentication.");
        Assert.True(antiforgeryIndex > authenticationIndex, "Expected browser mutation antiforgery to run after authentication.");
        Assert.True(authorizationIndex > antiforgeryIndex, "Expected browser mutation antiforgery to run before authorization.");
    }

    [Fact]
    public void ApiHostMayNotImportModuleApiNamespaces()
    {
        var apiHostSourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src", "ApiHost");
        var moduleApiAssemblyNames = RepositoryFiles
            .ReadCSharpFilesUnder("src", "Modules")
            .Where(path => path.Contains(".Api/", StringComparison.Ordinal))
            .Select(path =>
            {
                var segments = path.Split('/');
                var index = Array.FindIndex(segments, segment => segment.EndsWith(".Api", StringComparison.Ordinal));
                return index >= 0 ? segments[index] : string.Empty;
            })
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(moduleApiAssemblyNames);

        foreach (var relativePath in apiHostSourceFiles)
        {
            var text = RepositoryFiles.ReadAllText(relativePath);
            foreach (var moduleApiNamespace in moduleApiAssemblyNames)
            {
                Assert.DoesNotContain(
                    $"using {moduleApiNamespace};",
                    text,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ApiHostProjectCopiesBuiltFrontendAssetsIntoWwwroot()
    {
        var projectSource = RepositoryFiles.ReadAllText("src/ApiHost/ApiHost.csproj");

        Assert.Contains("..\\..\\web\\dist\\", projectSource, StringComparison.Ordinal);
        Assert.Contains("<Link>wwwroot\\%(RecursiveDir)%(Filename)%(Extension)</Link>", projectSource, StringComparison.Ordinal);
        Assert.Contains("<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>", projectSource, StringComparison.Ordinal);
        Assert.Contains("<CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>", projectSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentitySessionRevocationRequiresExplicitAdminRoleAndRecentAuthentication()
    {
        var request = new RevokeIdentityUserSessionsCommand("identity:user:operator");

        Assert.Contains(
            request.AuthorizationRequirements,
            requirement => requirement.AllowedRoles.Contains(IdentityRoles.Admin));
        Assert.Equal(Duration.FromMinutes(5), request.RecentAuthenticationWindow);
    }

    [Fact]
    public void MachineBootstrapRequestRequiresMachineRole()
    {
        var request = new GetMachineBootstrapManifestQuery();

        Assert.Contains(
            request.AuthorizationRequirements,
            requirement => requirement.AllowedRoles.Contains(IdentityRoles.Machine));
    }

    [Fact]
    public void IdentityAndPlatformSourceDeclareSeparateMachineAuthenticationPath()
    {
        var identityModuleSource = RepositoryFiles.ReadAllText("src/Modules/Identity/Identity.Api/IdentityModule.cs");
        var platformModuleSource = RepositoryFiles.ReadAllText("src/Modules/Platform/Platform.Api/PlatformModule.cs");

        Assert.Contains("AddScheme<AuthenticationSchemeOptions, MachineAuthenticationHandler>", identityModuleSource, StringComparison.Ordinal);
        Assert.Contains($"IdentityMachineAuthenticationDefaults.SchemeName", identityModuleSource, StringComparison.Ordinal);
        Assert.Contains("\"/machine/bootstrap\"", platformModuleSource, StringComparison.Ordinal);
        Assert.Contains("AuthenticateAsync(\"Machine\")", platformModuleSource, StringComparison.Ordinal);
        Assert.Equal("Machine", IdentityMachineAuthenticationDefaults.SchemeName);
    }

    [Fact]
    public void RuntimeSourceDoesNotUseDirectUtcNow()
    {
        var sourceFiles = RepositoryFiles.ReadCSharpFilesUnder("src")
            .Where(path => !path.Contains("/Migrations/", StringComparison.Ordinal))
            .ToArray();

        foreach (var relativePath in sourceFiles)
        {
            var text = RepositoryFiles.ReadAllText(relativePath);
            Assert.DoesNotContain("DateTimeOffset.UtcNow", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.UtcNow", text, StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<string> ReadEndpointRegistrations(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Regex.Matches(text, @"\b\w+\.Map(?:Get|Post|Put|Delete|Patch)\(", RegexOptions.CultureInvariant)
            .Select(match => ReadEndpointRegistration(text, match.Index))
            .ToArray();
    }

    private static string ReadEndpointRegistration(string text, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(text);

        var parenthesisDepth = 0;
        var insideString = false;
        var insideVerbatimString = false;
        var insideCharacter = false;
        var escaping = false;

        for (var index = startIndex; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (insideCharacter)
            {
                if (!escaping && current == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (!escaping && current == '\'')
                {
                    insideCharacter = false;
                }

                escaping = false;
                continue;
            }

            if (insideString)
            {
                if (!escaping && current == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (!escaping && current == '"')
                {
                    insideString = false;
                }

                escaping = false;
                continue;
            }

            if (insideVerbatimString)
            {
                if (current == '"' && next == '"')
                {
                    index++;
                    continue;
                }

                if (current == '"')
                {
                    insideVerbatimString = false;
                }

                continue;
            }

            if (current == '@' && next == '"')
            {
                insideVerbatimString = true;
                index++;
                continue;
            }

            if (((current == '$' && next == '@') || (current == '@' && next == '$'))
                && index + 2 < text.Length
                && text[index + 2] == '"')
            {
                insideVerbatimString = true;
                index += 2;
                continue;
            }

            if (current == '$' && next == '"')
            {
                insideString = true;
                index++;
                continue;
            }

            if (current == '"')
            {
                insideString = true;
                continue;
            }

            if (current == '\'')
            {
                insideCharacter = true;
                escaping = false;
                continue;
            }

            if (current == '(')
            {
                parenthesisDepth++;
                continue;
            }

            if (current == ')')
            {
                parenthesisDepth--;
                continue;
            }

            if (current == ';' && parenthesisDepth == 0)
            {
                return text[startIndex..(index + 1)];
            }
        }

        throw new InvalidOperationException("Failed to read a complete endpoint registration block from source.");
    }

    private static bool IsAllowlistedInfrastructureEndpoint(string relativePath, string endpointRegistration)
    {
        return string.Equals(relativePath, "src/Modules/Identity/Identity.Api/Endpoints/AntiforgeryEndpoints.cs", StringComparison.Ordinal)
            && endpointRegistration.Contains("Identity_GetAntiforgeryToken", StringComparison.Ordinal);
    }

    private static bool RoutesThroughDispatcher(string endpointRegistration)
    {
        return endpointRegistration.Contains("dispatcher.Send(", StringComparison.Ordinal)
            || endpointRegistration.Contains("dispatcher.Query(", StringComparison.Ordinal);
    }

    private static string DescribeEndpoint(string relativePath, string endpointRegistration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointRegistration);

        var namedMatch = Regex.Match(endpointRegistration, @"\.WithName\(""(?<name>[^""]+)""\)", RegexOptions.CultureInvariant);
        if (namedMatch.Success)
        {
            return $"endpoint {namedMatch.Groups["name"].Value} in {relativePath}";
        }

        return $"endpoint in {relativePath}";
    }
}
