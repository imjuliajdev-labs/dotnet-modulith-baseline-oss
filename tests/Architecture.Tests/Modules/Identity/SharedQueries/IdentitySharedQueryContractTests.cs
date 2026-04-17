using Identity.PublicContracts.Queries;

namespace Architecture.Tests.Modules.Identity.SharedQueries;

public sealed class IdentitySharedQueryContractTests
{
    [Fact]
    public void PreferredTimeZoneQueryServiceLivesInPublicContractsAndExposesStableLookupSignature()
    {
        var contractType = typeof(IIdentityTimeZonePreferenceQueryService);

        Assert.Equal("Identity.PublicContracts", contractType.Assembly.GetName().Name);

        var method = contractType.GetMethod(nameof(IIdentityTimeZonePreferenceQueryService.GetPreferredTimeZoneIdAsync));
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.Equal(typeof(ValueTask<string?>), method.ReturnType);
    }
}
