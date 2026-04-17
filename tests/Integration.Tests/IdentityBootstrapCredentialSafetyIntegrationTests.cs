using Xunit;

namespace Integration.Tests;

public sealed class IdentityBootstrapCredentialSafetyIntegrationTests
{
    [Fact]
    public async Task ProductionLikeCompositionRejectsSeededAdminPasswordWithoutOverride()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("baseline-seeded-admin-guard");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresBackedApiApplication.StartAsync(
                postgres.GetConnectionString(),
                configurationOverrides: new Dictionary<string, string?>
                {
                    [Identity.Infrastructure.Authentication.IdentityBootstrapCredentialPolicy.SeededMachineApiKeyConfigurationKey] = null
                },
                environmentName: "Production"));

        Assert.Contains(Identity.Infrastructure.Authentication.IdentityBootstrapCredentialPolicy.SeededAdminPasswordConfigurationKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionLikeCompositionRejectsSeededMachineApiKeyWithoutOverride()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("baseline-seeded-machine-guard");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresBackedApiApplication.StartAsync(
                postgres.GetConnectionString(),
                configurationOverrides: new Dictionary<string, string?>
                {
                    [Identity.Infrastructure.Authentication.IdentityBootstrapCredentialPolicy.SeededAdminPasswordConfigurationKey] = null
                },
                environmentName: "Production"));

        Assert.Contains(Identity.Infrastructure.Authentication.IdentityBootstrapCredentialPolicy.SeededMachineApiKeyConfigurationKey, exception.Message, StringComparison.Ordinal);
    }
}
