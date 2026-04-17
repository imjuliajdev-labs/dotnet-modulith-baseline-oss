using BuildingBlocks.Infrastructure.Modules;
using Identity.Api;

namespace Architecture.Tests.Modules.Identity;

public sealed class IdentityArchitectureTests
{
    [Xunit.Fact]
    public void ModuleEntryPointImplementsTheGovernedApiModuleShape()
    {
        var module = new IdentityModule();

        Xunit.Assert.IsAssignableFrom<IApiModule>(module);
        Xunit.Assert.Equal("identity", module.Key);
        Xunit.Assert.Equal("identity", module.Descriptor.ModuleNamespace);
    }
}
