using Platform.Api;

namespace Module.UnitTests.ModuleCoverage.Platform;

public sealed class PlatformModuleDescriptorTests
{
    [Xunit.Fact]
    public void DescriptorMatchesTheModuleSpec()
    {
        var module = new PlatformModule();

        Xunit.Assert.Equal("platform", module.Key);
        Xunit.Assert.Equal("Platform", module.Descriptor.DisplayName);
        Xunit.Assert.Equal("/platform", module.Descriptor.RoutePrefix);
        Xunit.Assert.Equal("platform", module.Descriptor.SchemaName);
        Xunit.Assert.Equal("platform", module.Descriptor.ModuleNamespace);
        Xunit.Assert.True(module.Descriptor.DefaultEnabled);
        Xunit.Assert.False(module.Descriptor.CanBeDisabled);
        Xunit.Assert.True(module.Descriptor.HasFrontendSurface);
    }
}
