using Blog.Api;

namespace Module.UnitTests.ModuleCoverage.Blog;

public sealed class BlogModuleDescriptorTests
{
    [Xunit.Fact]
    public void DescriptorMatchesTheModuleSpec()
    {
        var module = new BlogModule();

        Xunit.Assert.Equal("blog", module.Key);
        Xunit.Assert.Equal("Blog", module.Descriptor.DisplayName);
        Xunit.Assert.Equal("/blog", module.Descriptor.RoutePrefix);
        Xunit.Assert.Equal("blog", module.Descriptor.SchemaName);
        Xunit.Assert.Equal("blog", module.Descriptor.ModuleNamespace);
        Xunit.Assert.True(module.Descriptor.DefaultEnabled);
        Xunit.Assert.True(module.Descriptor.CanBeDisabled);
        Xunit.Assert.True(module.Descriptor.HasFrontendSurface);
    }
}
