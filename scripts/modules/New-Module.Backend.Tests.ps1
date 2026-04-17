function New-ModuleDescriptorTestContent {
    $defaultEnabledAssertion = if ([bool]$spec['defaultEnabled']) {
        'Xunit.Assert.True(module.Descriptor.DefaultEnabled);'
    } else {
        'Xunit.Assert.False(module.Descriptor.DefaultEnabled);'
    }

    $canBeDisabledAssertion = if (-not [bool]$spec['isCore']) {
        'Xunit.Assert.True(module.Descriptor.CanBeDisabled);'
    } else {
        'Xunit.Assert.False(module.Descriptor.CanBeDisabled);'
    }

    $hasFrontendSurfaceAssertion = if ([bool]$spec['hasFrontendSurface']) {
        'Xunit.Assert.True(module.Descriptor.HasFrontendSurface);'
    } else {
        'Xunit.Assert.False(module.Descriptor.HasFrontendSurface);'
    }

@"
using ${moduleName}.Api;

namespace Module.UnitTests.ModuleCoverage.${moduleName};

public sealed class ${moduleName}ModuleDescriptorTests
{
    [Xunit.Fact]
    public void DescriptorMatchesTheModuleSpec()
    {
        var module = new ${moduleName}Module();

        Xunit.Assert.Equal("$moduleKey", module.Key);
        Xunit.Assert.Equal("$moduleDisplayName", module.Descriptor.DisplayName);
        Xunit.Assert.Equal("$([string]$spec['routePrefix'])", module.Descriptor.RoutePrefix);
        Xunit.Assert.Equal("$([string]$spec['schemaName'])", module.Descriptor.SchemaName);
        Xunit.Assert.Equal("$moduleNamespace", module.Descriptor.ModuleNamespace);
        $defaultEnabledAssertion
        $canBeDisabledAssertion
        $hasFrontendSurfaceAssertion
    }
}
"@
}

function New-ModuleArchitectureTestContent {
@"
using BuildingBlocks.Infrastructure.Modules;
using ${moduleName}.Api;

namespace Architecture.Tests.ModuleCoverage.${moduleName};

public sealed class ${moduleName}ArchitectureTests
{
    [Xunit.Fact]
    public void ModuleEntryPointImplementsTheGovernedApiModuleShape()
    {
        var module = new ${moduleName}Module();

        Xunit.Assert.IsAssignableFrom<IApiModule>(module);
        Xunit.Assert.Equal("$moduleKey", module.Key);
        Xunit.Assert.Equal("$moduleNamespace", module.Descriptor.ModuleNamespace);
    }
}
"@
}

function New-BootstrapManifestIntegrationTestContent {
@"
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;

namespace Integration.Tests.ModuleCoverage.${moduleName};

public sealed class ${moduleName}BootstrapManifestTests
{
    [Xunit.Fact]
    public async Task PlatformBootstrapManifestIncludesTheScaffoldedModule()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var response = await client.GetAsync("/api/v1/platform/bootstrap");

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var modules = document.RootElement.GetProperty("modules").EnumerateArray().ToArray();

        Xunit.Assert.Contains(modules, element =>
            string.Equals(element.GetProperty("key").GetString(), "$moduleKey", StringComparison.Ordinal)
            && string.Equals(element.GetProperty("displayName").GetString(), "$moduleDisplayName", StringComparison.Ordinal));
    }
}
"@
}

function New-RecentAuthenticationPolicyContent {
@"
using NodaTime;

namespace ${moduleName}.Application.Authorization;

public static class ${moduleRecentAuthenticationPolicyTypeName}
{
    public static Duration SensitiveMutationWindow { get; } = Duration.FromMinutes(5);
}
"@
}

function New-RecentAuthenticationPolicyArchitectureTestContent {
@"
using NodaTime;
using ${moduleName}.Application.Authorization;

namespace Architecture.Tests.ModuleCoverage.${moduleName}.Authorization;

public sealed class ${moduleName}RecentAuthenticationPolicyTests
{
    [Xunit.Fact]
    public void RecentAuthenticationPolicyUsesTheGovernedSensitiveMutationWindow()
    {
        Xunit.Assert.Equal("${moduleName}.Application", typeof(${moduleRecentAuthenticationPolicyTypeName}).Assembly.GetName().Name);
        Xunit.Assert.Equal(Duration.FromMinutes(5), ${moduleRecentAuthenticationPolicyTypeName}.SensitiveMutationWindow);
    }
}
"@
}
