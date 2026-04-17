using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Modules;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
});
builder.Services.AddBuildingBlocksInfrastructureDefaults();
builder.Services.AddApiModulesFromAssemblyReferences(typeof(Program).Assembly);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DbMigrator");
var runner = host.Services.GetRequiredService<IDatabaseMigrationRunner>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    logger.LogInformation("Applying configured database migrations.");
    await runner.ApplyConfiguredMigrationsAsync(cts.Token);
    logger.LogInformation("Database migrations completed successfully.");
    return 0;
}
catch (OperationCanceledException)
{
    logger.LogWarning("Database migration cancelled.");
    return 1;
}
catch (Exception exception)
{
    logger.LogError(exception, "Database migration bootstrap failed.");
    return 1;
}
