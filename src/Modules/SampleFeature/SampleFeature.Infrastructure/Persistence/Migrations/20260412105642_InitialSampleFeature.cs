using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SampleFeature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSampleFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE SCHEMA IF NOT EXISTS sample_feature;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'sample_feature'
                            AND table_name = 'announcements') THEN
                        CREATE TABLE sample_feature.announcements
                        (
                            announcement_id UUID NOT NULL,
                            title TEXT NOT NULL,
                            body TEXT NOT NULL,
                            published_utc TIMESTAMPTZ NOT NULL,
                            published_by_actor_id TEXT NOT NULL,
                            CONSTRAINT "PK_announcements" PRIMARY KEY (announcement_id)
                        );
                    END IF;
                END $$;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'sample_feature'
                            AND table_name = 'scheduled_announcements') THEN
                        CREATE TABLE sample_feature.scheduled_announcements
                        (
                            scheduled_announcement_id UUID NOT NULL,
                            title TEXT NOT NULL,
                            body TEXT NOT NULL,
                            scheduled_local_date DATE NOT NULL,
                            scheduled_local_time TIME WITHOUT TIME ZONE NOT NULL,
                            time_zone_id TEXT NOT NULL,
                            scheduled_for_utc TIMESTAMPTZ NOT NULL,
                            scheduled_by_actor_id TEXT NOT NULL,
                            local_time_resolution TEXT NOT NULL,
                            status TEXT NOT NULL,
                            created_utc TIMESTAMPTZ NOT NULL,
                            updated_utc TIMESTAMPTZ NOT NULL,
                            published_announcement_id UUID NULL,
                            published_utc TIMESTAMPTZ NULL,
                            lease_id UUID NULL,
                            lease_until_utc TIMESTAMPTZ NULL,
                            CONSTRAINT "PK_scheduled_announcements" PRIMARY KEY (scheduled_announcement_id)
                        );
                    END IF;
                END $$;

                CREATE INDEX IF NOT EXISTS ix_sample_feature_scheduled_announcements_actor
                    ON sample_feature.scheduled_announcements (scheduled_by_actor_id ASC, scheduled_for_utc DESC);

                CREATE INDEX IF NOT EXISTS ix_sample_feature_scheduled_announcements_due
                    ON sample_feature.scheduled_announcements (status, scheduled_for_utc);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcements",
                schema: "sample_feature");

            migrationBuilder.DropTable(
                name: "scheduled_announcements",
                schema: "sample_feature");
        }
    }
}
