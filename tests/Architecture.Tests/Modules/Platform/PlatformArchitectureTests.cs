using BuildingBlocks.Infrastructure.Modules;
using Platform.Api;

namespace Architecture.Tests.Modules.Platform;

public sealed class PlatformArchitectureTests
{
    [Xunit.Fact]
    public void ModuleEntryPointImplementsTheGovernedApiModuleShape()
    {
        var module = new PlatformModule();

        Xunit.Assert.IsAssignableFrom<IApiModule>(module);
        Xunit.Assert.Equal("platform", module.Key);
        Xunit.Assert.Equal("platform", module.Descriptor.ModuleNamespace);
    }
}
