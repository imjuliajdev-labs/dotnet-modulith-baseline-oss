using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Authentication;

public static class IdentityBootstrapCredentialPolicy
{
    public const string SeededAdminPasswordConfigurationKey = "Modules:Identity:SeededAdmin:Password";
    public const string SeededMachineApiKeyConfigurationKey = "Modules:Identity:SeededMachine:ApiKey";

    internal static void EnsureConfiguredSeededCredentialsAllowed(
        IHostEnvironment hostEnvironment,
        SeededAdminOptions seededAdminOptions,
        SeededMachineOptions seededMachineOptions)
    {
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        ArgumentNullException.ThrowIfNull(seededAdminOptions);
        ArgumentNullException.ThrowIfNull(seededMachineOptions);

        if (IsDevelopmentLike(hostEnvironment))
        {
            return;
        }

        var configuredKeys = new List<string>();

        if (!string.IsNullOrWhiteSpace(seededAdminOptions.Password))
        {
            configuredKeys.Add(SeededAdminPasswordConfigurationKey);
        }

        if (!string.IsNullOrWhiteSpace(seededMachineOptions.ApiKey))
        {
            configuredKeys.Add(SeededMachineApiKeyConfigurationKey);
        }

        if (configuredKeys.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Seeded identity bootstrap credentials are development/test-only. Environment '{hostEnvironment.EnvironmentName}' must not rely on {string.Join(", ", configuredKeys.OrderBy(static key => key, StringComparer.Ordinal))}. Remove the configured seeded credentials in non-development environments.");
    }

    private static bool IsDevelopmentLike(IHostEnvironment hostEnvironment)
    {
        if (hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Testing"))
        {
            return true;
        }

        var aspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(aspNetCoreEnvironment, Environments.Development, StringComparison.OrdinalIgnoreCase)
            || string.Equals(aspNetCoreEnvironment, "Testing", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class IdentityBootstrapCredentialValidationHostedService : IHostedService
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly SeededAdminOptions _seededAdminOptions;
    private readonly SeededMachineOptions _seededMachineOptions;

    public IdentityBootstrapCredentialValidationHostedService(
        IHostEnvironment hostEnvironment,
        IOptions<SeededAdminOptions> seededAdminOptions,
        IOptions<SeededMachineOptions> seededMachineOptions)
    {
        _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
        ArgumentNullException.ThrowIfNull(seededAdminOptions);
        ArgumentNullException.ThrowIfNull(seededMachineOptions);

        _seededAdminOptions = seededAdminOptions.Value;
        _seededMachineOptions = seededMachineOptions.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        IdentityBootstrapCredentialPolicy.EnsureConfiguredSeededCredentialsAllowed(
            _hostEnvironment,
            _seededAdminOptions,
            _seededMachineOptions);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
