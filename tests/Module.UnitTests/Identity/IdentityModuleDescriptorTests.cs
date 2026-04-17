using Identity.Api;

namespace Module.UnitTests.ModuleCoverage.Identity;

public sealed class IdentityModuleDescriptorTests
{
    [Xunit.Fact]
    public void DescriptorMatchesTheModuleSpec()
    {
        var module = new IdentityModule();

        Xunit.Assert.Equal("identity", module.Key);
        Xunit.Assert.Equal("Identity", module.Descriptor.DisplayName);
        Xunit.Assert.Equal("/identity", module.Descriptor.RoutePrefix);
        Xunit.Assert.Equal("identity", module.Descriptor.SchemaName);
        Xunit.Assert.Equal("identity", module.Descriptor.ModuleNamespace);
        Xunit.Assert.True(module.Descriptor.DefaultEnabled);
        Xunit.Assert.False(module.Descriptor.CanBeDisabled);
        Xunit.Assert.False(module.Descriptor.HasFrontendSurface);
    }
}
