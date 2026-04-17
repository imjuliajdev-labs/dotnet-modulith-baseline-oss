using Admin.Api;

namespace Module.UnitTests.ModuleCoverage.Admin;

public sealed class AdminModuleDescriptorTests
{
    [Xunit.Fact]
    public void DescriptorMatchesTheModuleSpec()
    {
        var module = new AdminModule();

        Xunit.Assert.Equal("admin", module.Key);
        Xunit.Assert.Equal("Admin", module.Descriptor.DisplayName);
        Xunit.Assert.Equal("/admin", module.Descriptor.RoutePrefix);
        Xunit.Assert.Equal("admin", module.Descriptor.SchemaName);
        Xunit.Assert.Equal("admin", module.Descriptor.ModuleNamespace);
        Xunit.Assert.True(module.Descriptor.DefaultEnabled);
        Xunit.Assert.True(module.Descriptor.CanBeDisabled);
        Xunit.Assert.True(module.Descriptor.HasFrontendSurface);
    }
}
