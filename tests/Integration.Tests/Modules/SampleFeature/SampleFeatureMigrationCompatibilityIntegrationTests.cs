using System.Net;
using Microsoft.AspNetCore.TestHost;
using Npgsql;

namespace Integration.Tests.ModuleCoverage.SampleFeature;

public sealed class SampleFeatureMigrationCompatibilityIntegrationTests
{
    [Xunit.Fact]
    public async Task SampleFeatureMigrationResumesFromPartiallyAppliedInitialSchema()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("sample_feature_migration_resume");
        var connectionString = postgres.GetConnectionString();

        await SeedPartiallyAppliedInitialSchemaAsync(connectionString);

        await using var application = await PostgresBackedApiApplication.StartAsync(connectionString);

        Xunit.Assert.True(await HasMigrationAsync(connectionString, "20260412105642_InitialSampleFeature"));
        Xunit.Assert.True(await IndexExistsAsync(connectionString, "sample_feature", "ix_sample_feature_scheduled_announcements_actor"));
        Xunit.Assert.True(await IndexExistsAsync(connectionString, "sample_feature", "ix_sample_feature_scheduled_announcements_due"));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var response = await client.GetAsync("/api/v1/platform/bootstrap");
        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task SeedPartiallyAppliedInitialSchemaAsync(string connectionString)
    {
        const string sql = """
            CREATE SCHEMA IF NOT EXISTS sample_feature;

            CREATE TABLE IF NOT EXISTS sample_feature.announcements
            (
                announcement_id uuid NOT NULL,
                title text NOT NULL,
                body text NOT NULL,
                published_utc timestamp with time zone NOT NULL,
                published_by_actor_id text NOT NULL,
                CONSTRAINT "PK_announcements" PRIMARY KEY (announcement_id)
            );

            CREATE TABLE IF NOT EXISTS sample_feature.scheduled_announcements
            (
                scheduled_announcement_id uuid NOT NULL,
                title text NOT NULL,
                body text NOT NULL,
                scheduled_local_date date NOT NULL,
                scheduled_local_time time without time zone NOT NULL,
                time_zone_id text NOT NULL,
                scheduled_for_utc timestamp with time zone NOT NULL,
                scheduled_by_actor_id text NOT NULL,
                local_time_resolution text NOT NULL,
                status text NOT NULL,
                created_utc timestamp with time zone NOT NULL,
                updated_utc timestamp with time zone NOT NULL,
                published_announcement_id uuid NULL,
                published_utc timestamp with time zone NULL,
                lease_id uuid NULL,
                lease_until_utc timestamp with time zone NULL,
                CONSTRAINT "PK_scheduled_announcements" PRIMARY KEY (scheduled_announcement_id)
            );
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HasMigrationAsync(string connectionString, string migrationId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM sample_feature."__EFMigrationsHistory"
            WHERE "MigrationId" = @migrationId;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("migrationId", migrationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> IndexExistsAsync(string connectionString, string schemaName, string indexName)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = @schemaName
              AND indexname = @indexName;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("indexName", indexName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }
}
