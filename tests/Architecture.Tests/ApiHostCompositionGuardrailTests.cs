using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Infrastructure.Configuration;

namespace Architecture.Tests;

public sealed class ApiHostCompositionGuardrailTests
{
    [Fact]
    public void ApiHostCompositionDoesNotUseWildcardCorsHelpers()
    {
        var compositionSource = RepositoryFiles.ReadAllText("src/ApiHost/ApiHostComposition.cs");

        Assert.DoesNotContain("AllowAnyMethod", compositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnyHeader", compositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnyOrigin", compositionSource, StringComparison.Ordinal);
        Assert.Contains(".WithMethods(", compositionSource, StringComparison.Ordinal);
        Assert.Contains(".WithHeaders(", compositionSource, StringComparison.Ordinal);
        Assert.Contains(".AllowCredentials()", compositionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendCorsOptionsDefaultsAreExplicitAllowLists()
    {
        Assert.NotEmpty(ApiHost.FrontendCorsOptions.DefaultAllowedMethods);
        Assert.NotEmpty(ApiHost.FrontendCorsOptions.DefaultAllowedHeaders);
        Assert.DoesNotContain("*", ApiHost.FrontendCorsOptions.DefaultAllowedMethods);
        Assert.DoesNotContain("*", ApiHost.FrontendCorsOptions.DefaultAllowedHeaders);
        Assert.Contains("X-CSRF-TOKEN", ApiHost.FrontendCorsOptions.DefaultAllowedHeaders);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PlatformRateLimiterOptionsRejectsNonPositiveModuleStateMutationPermitLimit(int moduleStateMutationPermitLimit)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{PlatformRateLimiterOptions.SectionName}:{nameof(PlatformRateLimiterOptions.ModuleStateMutationPermitLimit)}"]
                    = moduleStateMutationPermitLimit.ToString(CultureInfo.InvariantCulture)
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions<PlatformRateLimiterOptions>()
            .Configure<IConfiguration>(static (options, config) =>
            {
                config.GetSection(PlatformRateLimiterOptions.SectionName).Bind(options);
            })
            .Validate(
                static options => options.ModuleStateMutationPermitLimit > 0,
                "Platform rate limiter configuration requires a positive module-state mutation permit limit.");

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<PlatformRateLimiterOptions>>().Value);
        Assert.Contains(
            "Platform rate limiter configuration requires a positive module-state mutation permit limit.",
            exception.Message,
            StringComparison.Ordinal);
    }
}
