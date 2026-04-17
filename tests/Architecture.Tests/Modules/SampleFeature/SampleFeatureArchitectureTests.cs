using BuildingBlocks.Infrastructure.Modules;
using SampleFeature.Api;

namespace Architecture.Tests.Modules.SampleFeature;

public sealed class SampleFeatureArchitectureTests
{
    [Xunit.Fact]
    public void ModuleEntryPointImplementsTheGovernedApiModuleShape()
    {
        var module = new SampleFeatureModule();

        Xunit.Assert.IsAssignableFrom<IApiModule>(module);
        Xunit.Assert.Equal("sample-feature", module.Key);
        Xunit.Assert.Equal("sample-feature", module.Descriptor.ModuleNamespace);
    }
}
