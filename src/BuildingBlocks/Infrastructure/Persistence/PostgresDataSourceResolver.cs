using System.Collections.Concurrent;
using Npgsql;
using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Infrastructure.Persistence;

public interface IPostgresDataSourceResolver
{
    string? GetConnectionString(string connectionStringName);

    NpgsqlDataSource GetRequiredDataSource(string connectionStringName);
}

internal sealed class ConfiguredPostgresDataSourceResolver : IPostgresDataSourceResolver, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, Lazy<PostgresDataSourceRegistration>> _registrations = new(StringComparer.Ordinal);

    public ConfiguredPostgresDataSourceResolver(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public string? GetConnectionString(string connectionStringName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        if (_registrations.TryGetValue(connectionStringName, out var registration)
            && registration.IsValueCreated)
        {
            return registration.Value.ConnectionString;
        }

        var connectionString = _configuration.GetConnectionString(connectionStringName);
        return string.IsNullOrWhiteSpace(connectionString)
            ? null
            : connectionString;
    }

    public NpgsqlDataSource GetRequiredDataSource(string connectionStringName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        return _registrations
            .GetOrAdd(
                connectionStringName,
                name => new Lazy<PostgresDataSourceRegistration>(
                    () => CreateRegistration(_configuration, name),
                    LazyThreadSafetyMode.ExecutionAndPublication))
            .Value
            .DataSource;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var registration in _registrations.Values)
        {
            if (!registration.IsValueCreated)
            {
                continue;
            }

            await registration.Value.DataSource.DisposeAsync();
        }
    }

    private static PostgresDataSourceRegistration CreateRegistration(IConfiguration configuration, string connectionStringName)
    {
        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (string.Equals(connectionStringName, SharedRuntimePersistenceDefaults.ConnectionStringName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(SharedRuntimePersistenceDefaults.MissingConnectionStringMessage);
            }

            throw new InvalidOperationException($"PostgreSQL data source requires ConnectionStrings:{connectionStringName}.");
        }

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        return new PostgresDataSourceRegistration(connectionString, builder.Build());
    }

    private sealed record PostgresDataSourceRegistration(string ConnectionString, NpgsqlDataSource DataSource);
}
