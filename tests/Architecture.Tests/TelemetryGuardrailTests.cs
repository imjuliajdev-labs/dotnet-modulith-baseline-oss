namespace Architecture.Tests;

public sealed class TelemetryGuardrailTests
{
    [Fact]
    public void ApiHostPassesTheHostEnvironmentIntoTelemetryRegistration()
    {
        var source = RepositoryFiles.ReadAllText("src/ApiHost/ApiHostComposition.cs");

        Assert.Contains("AddBaselineOpenTelemetry(builder.Configuration, builder.Environment)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TestingHostsSuppressTelemetryConsoleExporters()
    {
        var telemetrySource = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Telemetry/OpenTelemetryServiceCollectionExtensions.cs");
        var integrationHostSource = RepositoryFiles.ReadAllText("tests/Integration.Tests/PostgresBackedApiApplication.cs");

        Assert.Contains("environment.IsEnvironment(\"Testing\")", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("else if (enableConsoleExporters)", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("string environmentName = \"Testing\"", integrationHostSource, StringComparison.Ordinal);
        Assert.Contains("EnvironmentName = environmentName", integrationHostSource, StringComparison.Ordinal);
        Assert.Contains("builder.Logging.SetMinimumLevel(LogLevel.Warning)", integrationHostSource, StringComparison.Ordinal);
    }
}
