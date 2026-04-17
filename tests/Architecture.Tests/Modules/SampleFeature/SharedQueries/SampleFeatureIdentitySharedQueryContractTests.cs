using Identity.PublicContracts.Queries;
using SampleFeature.Application.Scheduling;

namespace Architecture.Tests.Modules.SampleFeature.SharedQueries;

public sealed class SampleFeatureIdentitySharedQueryContractTests
{
    [Fact]
    public void SchedulingApplicationDependsOnIdentityPublicContractsRatherThanIdentityApplication()
    {
        var sampleFeatureApplicationAssembly = typeof(ScheduleSampleAnnouncementCommand).Assembly;
        var referencedAssemblies = sampleFeatureApplicationAssembly.GetReferencedAssemblies();

        Assert.Equal("Identity.PublicContracts", typeof(IIdentityTimeZonePreferenceQueryService).Assembly.GetName().Name);
        Assert.Contains(referencedAssemblies, assembly => string.Equals(assembly.Name, "Identity.PublicContracts", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, assembly => string.Equals(assembly.Name, "Identity.Application", StringComparison.Ordinal));
    }
}
