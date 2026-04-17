using BuildingBlocks.Infrastructure.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Integration.Tests;

public sealed class SchemaIsolationIntegrationTests
{
    // Schema isolation is enforced by schema ownership. A small set of infrastructure tables
    // intentionally repeat per schema and should not be treated as cross-module leakage.
    private static readonly HashSet<string> RepeatablePerSchemaInfrastructureTables = new(StringComparer.Ordinal)
    {
        "integration_outbox",
        "__EFMigrationsHistory"
    };

    [Fact]
    public async Task ModuleSchemasAreIsolatedAtRuntime()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();
        var connectionString = application.App.Configuration.GetConnectionString("BaselineDatabase")!;
        var moduleSchemas = application.App.Services.GetServices<IApiModule>()
            .Select(static module => module.Descriptor.SchemaName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static schema => schema, StringComparer.Ordinal)
            .ToArray();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var schemas = await GetModuleSchemasAsync(connection, moduleSchemas);

        Assert.Contains("starter_runtime", schemas);
        foreach (var schema in schemas)
        {
            Assert.Contains(schema, moduleSchemas.Append("starter_runtime"));
        }

        foreach (var schema in schemas)
        {
            var tables = await GetTablesInSchemaAsync(connection, schema);
            Assert.NotEmpty(tables);

            foreach (var otherSchema in schemas.Where(s => s != schema))
            {
                var otherTables = await GetTablesInSchemaAsync(connection, otherSchema);

                foreach (var table in tables)
                {
                    if (RepeatablePerSchemaInfrastructureTables.Contains(table))
                    {
                        continue;
                    }

                    Assert.DoesNotContain(table, otherTables);
                }
            }
        }
    }

    [Fact]
    public async Task ModuleCannotQueryAnotherModulesSchemaWithRestrictedSearchPath()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();
        var connectionString = application.App.Configuration.GetConnectionString("BaselineDatabase")!;
        var moduleSchemas = application.App.Services.GetServices<IApiModule>()
            .Select(static module => module.Descriptor.SchemaName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static schema => schema, StringComparer.Ordinal)
            .ToArray();

        Assert.True(moduleSchemas.Length >= 2, "Schema isolation requires at least two composed module schemas.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var persistedSchemas = await GetModuleSchemasAsync(connection, moduleSchemas);
        var persistedModuleSchemas = persistedSchemas
            .Where(static schema => !string.Equals(schema, "starter_runtime", StringComparison.Ordinal))
            .OrderBy(static schema => schema, StringComparer.Ordinal)
            .ToArray();

        Assert.True(persistedModuleSchemas.Length >= 2, "Schema isolation requires at least two persisted module schemas with tables.");

        var sourceSchema = persistedModuleSchemas[0];
        var targetSchema = persistedModuleSchemas[1];

        await using var setPathCommand = new NpgsqlCommand($"SET search_path TO \"{sourceSchema}\"", connection);
        await setPathCommand.ExecuteNonQueryAsync();

        var targetTables = await GetTablesInSchemaAsync(connection, targetSchema);
        Assert.NotEmpty(targetTables);

        foreach (var table in targetTables)
        {
            await using var queryCommand = new NpgsqlCommand($"SELECT COUNT(*) FROM \"{table}\"", connection);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => queryCommand.ExecuteScalarAsync());
            Assert.Contains("does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<List<string>> GetModuleSchemasAsync(NpgsqlConnection connection, IEnumerable<string> moduleSchemas)
    {
        var knownSchemas = moduleSchemas
            .Append("starter_runtime")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existing = new List<string>();

        foreach (var schema in knownSchemas)
        {
            var tables = await GetTablesInSchemaAsync(connection, schema);
            if (tables.Count > 0)
            {
                existing.Add(schema);
            }
        }

        return existing;
    }

    private static async Task<List<string>> GetTablesInSchemaAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema ORDER BY table_name",
            connection);
        command.Parameters.AddWithValue("schema", schema);

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
