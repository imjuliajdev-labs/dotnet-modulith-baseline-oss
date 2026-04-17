using BuildingBlocks.Infrastructure.Modules;
using Blog.Api;

namespace Architecture.Tests.ModuleCoverage.Blog;

public sealed class BlogArchitectureTests
{
    [Xunit.Fact]
    public void ModuleEntryPointImplementsTheGovernedApiModuleShape()
    {
        var module = new BlogModule();

        Xunit.Assert.IsAssignableFrom<IApiModule>(module);
        Xunit.Assert.Equal("blog", module.Key);
        Xunit.Assert.Equal("blog", module.Descriptor.ModuleNamespace);
    }
}
