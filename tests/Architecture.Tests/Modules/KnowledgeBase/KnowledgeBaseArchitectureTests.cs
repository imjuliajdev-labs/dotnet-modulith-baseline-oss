using BuildingBlocks.Infrastructure.Modules;
using KnowledgeBase.Api;

namespace Architecture.Tests.Modules.KnowledgeBase;

public sealed class KnowledgeBaseArchitectureTests
{
    [Xunit.Fact]
    public void ModuleEntryPointImplementsTheGovernedApiModuleShape()
    {
        var module = new KnowledgeBaseModule();

        Xunit.Assert.IsAssignableFrom<IApiModule>(module);
        Xunit.Assert.Equal("knowledge-base", module.Key);
        Xunit.Assert.Equal("knowledge-base", module.Descriptor.ModuleNamespace);
    }
}
