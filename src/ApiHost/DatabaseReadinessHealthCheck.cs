using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

internal sealed class DatabaseReadinessHealthCheck : IHealthCheck
{
    private readonly IDatabaseMigrationRunner _migrationRunner;
    private readonly IHostEnvironment _environment;

    public DatabaseReadinessHealthCheck(IDatabaseMigrationRunner migrationRunner, IHostEnvironment environment)
    {
        _migrationRunner = migrationRunner ?? throw new ArgumentNullException(nameof(migrationRunner));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var statuses = await _migrationRunner.GetStatusAsync(cancellationToken);
            var pending = statuses
                .Where(static status => status.IsConfigured && status.PendingMigrations.Count > 0)
                .ToArray();

            if (pending.Length > 0)
            {
                var description = _environment.IsDevelopment()
                    ? $"Pending database migrations: {string.Join("; ", pending.Select(static s => $"{s.Name}: {s.PendingMigrations.Count} pending"))}"
                    : $"Pending database migrations: {pending.Length} source(s) behind";

                return HealthCheckResult.Unhealthy(description);
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            var description = _environment.IsDevelopment()
                ? "Failed to check database readiness."
                : "Database readiness check failed.";

            return _environment.IsDevelopment()
                ? HealthCheckResult.Unhealthy(description, exception)
                : HealthCheckResult.Unhealthy(description);
        }
    }
}
