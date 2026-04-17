using Admin.Api;
using BuildingBlocks.Infrastructure.Modules;

namespace Architecture.Tests.Modules.Admin;

public sealed class AdminArchitectureTests
{
    [Xunit.Fact]
    public void ModuleEntryPointImplementsTheGovernedApiModuleShape()
    {
        var module = new AdminModule();

        Xunit.Assert.IsAssignableFrom<IApiModule>(module);
        Xunit.Assert.Equal("admin", module.Key);
        Xunit.Assert.Equal("admin", module.Descriptor.ModuleNamespace);
    }
}
