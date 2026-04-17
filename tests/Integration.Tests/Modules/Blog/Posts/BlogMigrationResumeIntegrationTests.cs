using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blog.Api;
using Identity.Api.Authentication;
using Microsoft.AspNetCore.TestHost;
using Npgsql;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.ModuleCoverage.Blog.Posts;

public sealed class BlogMigrationResumeIntegrationTests
{
    [Xunit.Fact]
    public async Task BlogMigrationResumesFromPartiallyAppliedEditorialExpansionState()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("blog_migration_resume");
        var connectionString = postgres.GetConnectionString();
        var postId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await SeedPartiallyAppliedExpandMigrationAsync(connectionString, postId);

        await using var application = await PostgresBackedApiApplication.StartAsync(connectionString);

        Xunit.Assert.True(await HasMigrationAsync(connectionString, "20260408234626_ExpandBlogEditorialModel"));
        Xunit.Assert.True(await HasMigrationAsync(connectionString, "20260413004835_ContractBlogPostLegacyPascalCaseColumns"));
        Xunit.Assert.True(await IndexExistsAsync(connectionString, "blog", "ux_blog_posts_slug"));
        Xunit.Assert.True(await IndexExistsAsync(connectionString, "blog", "ix_blog_categories_name"));
        Xunit.Assert.True(await TableExistsAsync(connectionString, "blog", "blog_tags"));
        Xunit.Assert.True(await TableExistsAsync(connectionString, "blog", "blog_post_revisions"));
        Xunit.Assert.False(await ColumnExistsAsync(connectionString, "blog", "blog_posts", "Slug"));
        Xunit.Assert.False(await ColumnExistsAsync(connectionString, "blog", "blog_posts", "PostId"));

        var row = await GetBlogPostStateAsync(connectionString, postId);
        Xunit.Assert.Equal("partial-post", row.CanonicalSlug);
        Xunit.Assert.Equal("Partial post", row.CanonicalTitle);
        Xunit.Assert.Equal(string.Empty, row.Body);
        Xunit.Assert.False(row.Featured);
        Xunit.Assert.Equal(0, row.RevisionNumber);
        Xunit.Assert.Equal(0, row.ViewCount);

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        var categoriesResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/blog/manage/categories", adminSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
    }

    private static async Task SeedPartiallyAppliedExpandMigrationAsync(string connectionString, Guid postId)
    {
        const string sql = """
            CREATE SCHEMA IF NOT EXISTS blog;

            CREATE TABLE IF NOT EXISTS blog."__EFMigrationsHistory"
            (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            );

            INSERT INTO blog."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('20260408104718_InitialBlogPosts', '10.0.0')
            ON CONFLICT ("MigrationId") DO NOTHING;

            CREATE TABLE IF NOT EXISTS blog.blog_posts
            (
                "PostId" uuid NOT NULL,
                "Slug" character varying(128) NOT NULL,
                "Title" character varying(200) NOT NULL,
                "Summary" character varying(1024) NOT NULL,
                "Status" character varying(32) NOT NULL,
                "Version" integer NOT NULL,
                "CreatedUtc" timestamp with time zone NOT NULL,
                "UpdatedUtc" timestamp with time zone NOT NULL,
                "UpdatedByActorId" character varying(256) NOT NULL,
                "PublishedUtc" timestamp with time zone NULL,
                "PublishedByActorId" character varying(256) NULL,
                CONSTRAINT "PK_blog_posts" PRIMARY KEY ("PostId")
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_blog_posts_Slug" ON blog.blog_posts ("Slug");
            CREATE INDEX IF NOT EXISTS "IX_blog_posts_Status_PublishedUtc" ON blog.blog_posts ("Status", "PublishedUtc");

            INSERT INTO blog.blog_posts
                ("PostId", "Slug", "Title", "Summary", "Status", "Version", "CreatedUtc", "UpdatedUtc", "UpdatedByActorId", "PublishedUtc", "PublishedByActorId")
            VALUES
                (@postId, 'partial-post', 'Partial post', 'A partially applied migration row.', 'draft', 1, @createdUtc, @updatedUtc, 'identity:seeded-admin', NULL, NULL)
            ON CONFLICT ("PostId") DO NOTHING;

            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS post_id uuid;
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS slug character varying(200);
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS title character varying(256);
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS summary character varying(1024);
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS status character varying(32);
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS version integer;
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS created_utc timestamp with time zone;
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS updated_utc timestamp with time zone;
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS updated_by_actor_id character varying(256);
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS published_utc timestamp with time zone;
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS published_by_actor_id character varying(256);

            UPDATE blog.blog_posts
            SET post_id = COALESCE(post_id, "PostId"),
                slug = COALESCE(slug, "Slug"),
                title = COALESCE(title, "Title"),
                summary = COALESCE(summary, "Summary"),
                status = COALESCE(status, "Status"),
                version = COALESCE(version, "Version"),
                created_utc = COALESCE(created_utc, "CreatedUtc"),
                updated_utc = COALESCE(updated_utc, "UpdatedUtc"),
                updated_by_actor_id = COALESCE(updated_by_actor_id, "UpdatedByActorId"),
                published_utc = COALESCE(published_utc, "PublishedUtc"),
                published_by_actor_id = COALESCE(published_by_actor_id, "PublishedByActorId");

            ALTER TABLE blog.blog_posts ALTER COLUMN post_id SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN slug SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN title SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN summary SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN status SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN version SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN created_utc SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN updated_utc SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN updated_by_actor_id SET NOT NULL;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_constraint constraint_record
                    INNER JOIN pg_namespace schema_record ON schema_record.oid = constraint_record.connamespace
                    WHERE constraint_record.conname = 'AK_blog_posts_post_id'
                        AND schema_record.nspname = 'blog') THEN
                    ALTER TABLE blog.blog_posts ADD CONSTRAINT "AK_blog_posts_post_id" UNIQUE (post_id);
                END IF;
            END $$;

            CREATE UNIQUE INDEX IF NOT EXISTS ux_blog_posts_slug ON blog.blog_posts (slug);

            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS body text;
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS featured boolean;
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS revision_number integer;
            ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS view_count bigint;

            UPDATE blog.blog_posts
            SET body = COALESCE(body, ''),
                featured = COALESCE(featured, FALSE),
                revision_number = COALESCE(revision_number, 0),
                view_count = COALESCE(view_count, 0);

            ALTER TABLE blog.blog_posts ALTER COLUMN body SET DEFAULT '';
            ALTER TABLE blog.blog_posts ALTER COLUMN body SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN featured SET DEFAULT FALSE;
            ALTER TABLE blog.blog_posts ALTER COLUMN featured SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN revision_number SET DEFAULT 0;
            ALTER TABLE blog.blog_posts ALTER COLUMN revision_number SET NOT NULL;
            ALTER TABLE blog.blog_posts ALTER COLUMN view_count SET DEFAULT 0;
            ALTER TABLE blog.blog_posts ALTER COLUMN view_count SET NOT NULL;

            CREATE TABLE IF NOT EXISTS blog.blog_categories
            (
                slug character varying(128) NOT NULL,
                name character varying(256) NOT NULL,
                description character varying(1024) NULL,
                version integer NOT NULL,
                created_utc timestamp with time zone NOT NULL,
                updated_utc timestamp with time zone NOT NULL,
                updated_by_actor_id character varying(256) NOT NULL,
                CONSTRAINT "PK_blog_categories" PRIMARY KEY (slug)
            );

            CREATE INDEX IF NOT EXISTS ix_blog_categories_name ON blog.blog_categories (name);
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("postId", postId);
        command.Parameters.AddWithValue("createdUtc", new DateTimeOffset(2026, 04, 08, 10, 00, 00, TimeSpan.Zero));
        command.Parameters.AddWithValue("updatedUtc", new DateTimeOffset(2026, 04, 08, 10, 05, 00, TimeSpan.Zero));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HasMigrationAsync(string connectionString, string migrationId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM blog."__EFMigrationsHistory"
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

    private static async Task<bool> TableExistsAsync(string connectionString, string schemaName, string tableName)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @schemaName
              AND table_name = @tableName;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("tableName", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(string connectionString, string schemaName, string tableName, string columnName)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = @schemaName
              AND table_name = @tableName
              AND column_name = @columnName;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("tableName", tableName);
        command.Parameters.AddWithValue("columnName", columnName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<BlogPostState> GetBlogPostStateAsync(string connectionString, Guid postId)
    {
        const string sql = """
            SELECT slug, title, body, featured, revision_number, view_count
            FROM blog.blog_posts
            WHERE post_id = @postId;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("postId", postId);
        await using var reader = await command.ExecuteReaderAsync();

        Xunit.Assert.True(await reader.ReadAsync());
        return new BlogPostState(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetInt32(4),
            reader.GetInt64(5));
    }
    private sealed record BlogPostState(
        string CanonicalSlug,
        string CanonicalTitle,
        string Body,
        bool Featured,
        int RevisionNumber,
        long ViewCount);
}
