using SampleFeature.Api;

namespace Module.UnitTests.ModuleCoverage.SampleFeature;

public sealed class SampleFeatureModuleDescriptorTests
{
    [Xunit.Fact]
    public void DescriptorMatchesTheModuleSpec()
    {
        var module = new SampleFeatureModule();

        Xunit.Assert.Equal("sample-feature", module.Key);
        Xunit.Assert.Equal("Sample Feature", module.Descriptor.DisplayName);
        Xunit.Assert.Equal("/sample-feature", module.Descriptor.RoutePrefix);
        Xunit.Assert.Equal("sample_feature", module.Descriptor.SchemaName);
        Xunit.Assert.Equal("sample-feature", module.Descriptor.ModuleNamespace);
        Xunit.Assert.True(module.Descriptor.DefaultEnabled);
        Xunit.Assert.True(module.Descriptor.CanBeDisabled);
        Xunit.Assert.True(module.Descriptor.HasFrontendSurface);
    }
}
