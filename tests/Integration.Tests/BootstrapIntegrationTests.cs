namespace Integration.Tests;

public sealed class BootstrapIntegrationTests
{
    [Xunit.Fact]
    public void ApiHostAssemblyLoads()
    {
        Xunit.Assert.Equal("ApiHost", typeof(Program).Assembly.GetName().Name);
    }
}
