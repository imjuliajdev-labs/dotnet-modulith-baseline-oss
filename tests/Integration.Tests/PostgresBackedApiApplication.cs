using ApiHost;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Modules;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Integration.Tests;

internal sealed class PostgresBackedApiApplication : IAsyncDisposable
{
    private readonly PostgreSqlContainer? _postgres;

    private PostgresBackedApiApplication(PostgreSqlContainer? postgres, WebApplication app)
    {
        _postgres = postgres;
        App = app;
    }

    public WebApplication App { get; }

    public static async Task<PostgresBackedApiApplication> StartAsync(
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        int moduleStateMutationPermitLimit = 20,
        string? webRootPath = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<IServiceCollection>? configureExtraMigrations = null,
        string environmentName = "Testing")
    {
        var postgres = await StartPostgresAsync();
        var configuration = CreateConfiguration(postgres.GetConnectionString(), configurationOverrides, moduleStateMutationPermitLimit);

        try
        {
            return await StartAsync(
                configuration,
                applyMigrations: true,
                webRootPath,
                configureBuilder,
                postgres,
                environmentName: environmentName,
                configureExtraMigrations: configureExtraMigrations);
        }
        catch
        {
            await postgres.DisposeAsync();
            throw;
        }
    }

    public static Task<PostgresBackedApiApplication> StartAsync(
        string connectionString,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        int moduleStateMutationPermitLimit = 20,
        bool applyMigrations = true,
        string? webRootPath = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        IReadOnlyCollection<string>? removeHostedServices = null,
        Action<IServiceCollection>? configureExtraMigrations = null,
        string environmentName = "Testing")
    {
        var configuration = CreateConfiguration(connectionString, configurationOverrides, moduleStateMutationPermitLimit);
        return StartAsync(configuration, applyMigrations, webRootPath, configureBuilder, postgres: null, removeHostedServices, environmentName, configureExtraMigrations);
    }

    public static IReadOnlyDictionary<string, string?> CreateConfiguration(
        string connectionString,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        int moduleStateMutationPermitLimit = 20)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Modules:Identity:SeededAdmin:Password"] = "LocalOnly!123",
            ["Modules:Identity:SeededMachine:ApiKey"] = "MachineOnly!123",
            ["Modules:Identity:SeededMachine:Roles:0"] = "Machine",
            ["Modules:Platform:RateLimiter:ModuleStateMutationPermitLimit"] = moduleStateMutationPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [$"ConnectionStrings:{PlatformPersistenceDefaults.ConnectionStringName}"] = connectionString
        };

        if (configurationOverrides is not null)
        {
            foreach (var (key, value) in configurationOverrides)
            {
                configuration[key] = value;
            }
        }

        return configuration;
    }

    public static async Task<PostgreSqlContainer> StartPostgresAsync(string databaseName = "baseline")
    {
        var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase(databaseName)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();
        return postgres;
    }

    public static async Task RunDbMigratorAsync(
        IReadOnlyDictionary<string, string?> configuration,
        string environmentName = "Testing",
        Action<IServiceCollection>? configureExtraMigrations = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddApiModulesFromAssemblyReferences(typeof(Program).Assembly);
        configureExtraMigrations?.Invoke(services);

        await using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<IDatabaseMigrationRunner>();
        await runner.ApplyConfiguredMigrationsAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    private static async Task<PostgresBackedApiApplication> StartAsync(
        IReadOnlyDictionary<string, string?> configuration,
        bool applyMigrations,
        string? webRootPath,
        Action<WebApplicationBuilder>? configureBuilder,
        PostgreSqlContainer? postgres,
        IReadOnlyCollection<string>? removeHostedServices = null,
        string environmentName = "Testing",
        Action<IServiceCollection>? configureExtraMigrations = null)
    {
        if (applyMigrations)
        {
            await RunDbMigratorAsync(configuration, environmentName, configureExtraMigrations);
        }

        var builderOptions = string.IsNullOrWhiteSpace(webRootPath)
            ? new WebApplicationOptions
            {
                EnvironmentName = environmentName
            }
            : new WebApplicationOptions
            {
                WebRootPath = webRootPath,
                EnvironmentName = environmentName
            };

        var builder = WebApplication.CreateBuilder(builderOptions);
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Configuration.AddInMemoryCollection(configuration);
        configureBuilder?.Invoke(builder);

        builder.AddBaselineApiHostServices();
        RemoveHostedService(builder.Services, "IntegrationEventOutboxHostedService");

        if (removeHostedServices is not null)
        {
            foreach (var serviceName in removeHostedServices)
            {
                RemoveHostedService(builder.Services, serviceName);
            }
        }

        var app = builder.Build();
        app.MapBaselineApiHost();

        await app.StartAsync();
        return new PostgresBackedApiApplication(postgres, app);
    }

    private static void RemoveHostedService(IServiceCollection services, string implementationTypeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationTypeName);

        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType != typeof(IHostedService))
            {
                continue;
            }

            if (!string.Equals(descriptor.ImplementationType?.Name, implementationTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            services.RemoveAt(index);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
            ContentRootFileProvider = new PhysicalFileProvider(ContentRootPath);
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = typeof(Program).Assembly.GetName().Name ?? "Integration.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
