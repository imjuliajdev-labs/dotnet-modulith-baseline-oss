using KnowledgeBase.Api;

namespace Module.UnitTests.Modules.KnowledgeBase;

public sealed class KnowledgeBaseModuleDescriptorTests
{
    [Xunit.Fact]
    public void DescriptorMatchesTheModuleSpec()
    {
        var module = new KnowledgeBaseModule();

        Xunit.Assert.Equal("knowledge-base", module.Key);
        Xunit.Assert.Equal("Knowledge Base", module.Descriptor.DisplayName);
        Xunit.Assert.Equal("/knowledge-base", module.Descriptor.RoutePrefix);
        Xunit.Assert.Equal("knowledge_base", module.Descriptor.SchemaName);
        Xunit.Assert.Equal("knowledge-base", module.Descriptor.ModuleNamespace);
        Xunit.Assert.True(module.Descriptor.DefaultEnabled);
        Xunit.Assert.True(module.Descriptor.CanBeDisabled);
        Xunit.Assert.True(module.Descriptor.HasFrontendSurface);
    }
}
