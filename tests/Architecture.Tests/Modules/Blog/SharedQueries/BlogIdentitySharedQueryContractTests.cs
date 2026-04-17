using Blog.Application.Scheduling;
using Identity.PublicContracts.Queries;

namespace Architecture.Tests.Modules.Blog.SharedQueries;

public sealed class BlogIdentitySharedQueryContractTests
{
    [Fact]
    public void SchedulingApplicationDependsOnIdentityPublicContractsRatherThanIdentityApplication()
    {
        var blogApplicationAssembly = typeof(ScheduleBlogPostLifecycleCommand).Assembly;
        var referencedAssemblies = blogApplicationAssembly.GetReferencedAssemblies();

        Assert.Equal("Identity.PublicContracts", typeof(IIdentityTimeZonePreferenceQueryService).Assembly.GetName().Name);
        Assert.Contains(referencedAssemblies, assembly => string.Equals(assembly.Name, "Identity.PublicContracts", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, assembly => string.Equals(assembly.Name, "Identity.Application", StringComparison.Ordinal));
    }
}
