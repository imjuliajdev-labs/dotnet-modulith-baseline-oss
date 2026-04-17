namespace Module.UnitTests;

public sealed class BootstrapUnitTests
{
    [Xunit.Fact]
    public void DomainAssemblyLoads()
    {
        Xunit.Assert.Equal("BuildingBlocks.Domain", typeof(BuildingBlocks.Domain.Time.IClock).Assembly.GetName().Name);
    }
}
