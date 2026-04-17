using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandBlogEditorialModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
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

                ALTER TABLE blog.blog_posts ALTER COLUMN "Slug" TYPE character varying(200);
                ALTER TABLE blog.blog_posts ALTER COLUMN "Title" TYPE character varying(256);
                """);

            migrationBuilder.Sql(
                """
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

                CREATE OR REPLACE FUNCTION blog.sync_blog_posts_compatibility_columns()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    NEW.post_id := COALESCE(NEW.post_id, NEW."PostId");
                    NEW."PostId" := COALESCE(NEW."PostId", NEW.post_id);

                    NEW.slug := COALESCE(NEW.slug, NEW."Slug");
                    NEW."Slug" := COALESCE(NEW."Slug", NEW.slug);

                    NEW.title := COALESCE(NEW.title, NEW."Title");
                    NEW."Title" := COALESCE(NEW."Title", NEW.title);

                    NEW.summary := COALESCE(NEW.summary, NEW."Summary");
                    NEW."Summary" := COALESCE(NEW."Summary", NEW.summary);

                    NEW.status := COALESCE(NEW.status, NEW."Status");
                    NEW."Status" := COALESCE(NEW."Status", NEW.status);

                    NEW.version := COALESCE(NEW.version, NEW."Version");
                    NEW."Version" := COALESCE(NEW."Version", NEW.version);

                    NEW.created_utc := COALESCE(NEW.created_utc, NEW."CreatedUtc");
                    NEW."CreatedUtc" := COALESCE(NEW."CreatedUtc", NEW.created_utc);

                    NEW.updated_utc := COALESCE(NEW.updated_utc, NEW."UpdatedUtc");
                    NEW."UpdatedUtc" := COALESCE(NEW."UpdatedUtc", NEW.updated_utc);

                    NEW.updated_by_actor_id := COALESCE(NEW.updated_by_actor_id, NEW."UpdatedByActorId");
                    NEW."UpdatedByActorId" := COALESCE(NEW."UpdatedByActorId", NEW.updated_by_actor_id);

                    NEW.published_utc := COALESCE(NEW.published_utc, NEW."PublishedUtc");
                    NEW."PublishedUtc" := COALESCE(NEW."PublishedUtc", NEW.published_utc);

                    NEW.published_by_actor_id := COALESCE(NEW.published_by_actor_id, NEW."PublishedByActorId");
                    NEW."PublishedByActorId" := COALESCE(NEW."PublishedByActorId", NEW.published_by_actor_id);

                    RETURN NEW;
                END $$;

                DROP TRIGGER IF EXISTS trg_sync_blog_posts_compatibility_columns ON blog.blog_posts;

                CREATE TRIGGER trg_sync_blog_posts_compatibility_columns
                BEFORE INSERT OR UPDATE ON blog.blog_posts
                FOR EACH ROW
                EXECUTE FUNCTION blog.sync_blog_posts_compatibility_columns();
                """);

            migrationBuilder.Sql(
                """
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
                CREATE INDEX IF NOT EXISTS ix_blog_posts_status_published_utc ON blog.blog_posts (status, published_utc);
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS body text;
                ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS category_slug character varying(128);
                ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS featured boolean;
                ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS revision_number integer;
                ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS seo_description character varying(512);
                ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS seo_keywords character varying(512);
                ALTER TABLE blog.blog_posts ADD COLUMN IF NOT EXISTS seo_title character varying(256);
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
                """);

            migrationBuilder.Sql(
                """
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

                CREATE TABLE IF NOT EXISTS blog.blog_post_publication_schedules
                (
                    post_id uuid NOT NULL,
                    scheduled_publish_local_date date NULL,
                    scheduled_publish_local_time time without time zone NULL,
                    scheduled_publish_time_zone_id character varying(128) NULL,
                    scheduled_publish_local_time_resolution character varying(64) NULL,
                    scheduled_publish_for_utc timestamp with time zone NULL,
                    scheduled_publish_by_actor_id character varying(256) NULL,
                    scheduled_unpublish_local_date date NULL,
                    scheduled_unpublish_local_time time without time zone NULL,
                    scheduled_unpublish_time_zone_id character varying(128) NULL,
                    scheduled_unpublish_local_time_resolution character varying(64) NULL,
                    scheduled_unpublish_for_utc timestamp with time zone NULL,
                    scheduled_unpublish_by_actor_id character varying(256) NULL,
                    updated_utc timestamp with time zone NOT NULL,
                    updated_by_actor_id character varying(256) NOT NULL,
                    CONSTRAINT "PK_blog_post_publication_schedules" PRIMARY KEY (post_id),
                    CONSTRAINT "FK_blog_post_publication_schedules_blog_posts_post_id"
                        FOREIGN KEY (post_id)
                        REFERENCES blog.blog_posts (post_id)
                        ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS blog.blog_post_revisions
                (
                    id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    post_id uuid NOT NULL,
                    revision_number integer NOT NULL,
                    post_version integer NOT NULL,
                    title character varying(256) NOT NULL,
                    summary character varying(1024) NOT NULL,
                    body text NOT NULL,
                    featured boolean NOT NULL,
                    category_slug character varying(128) NULL,
                    tag_names_json text NOT NULL,
                    seo_title character varying(256) NULL,
                    seo_description character varying(512) NULL,
                    seo_keywords character varying(512) NULL,
                    share_targets_json text NOT NULL,
                    created_utc timestamp with time zone NOT NULL,
                    created_by_actor_id character varying(256) NOT NULL,
                    CONSTRAINT "FK_blog_post_revisions_blog_posts_post_id"
                        FOREIGN KEY (post_id)
                        REFERENCES blog.blog_posts (post_id)
                        ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS blog.blog_post_share_targets
                (
                    post_id uuid NOT NULL,
                    target character varying(64) NOT NULL,
                    CONSTRAINT "PK_blog_post_share_targets" PRIMARY KEY (post_id, target),
                    CONSTRAINT "FK_blog_post_share_targets_blog_posts_post_id"
                        FOREIGN KEY (post_id)
                        REFERENCES blog.blog_posts (post_id)
                        ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS blog.blog_post_tags
                (
                    post_id uuid NOT NULL,
                    tag_slug character varying(128) NOT NULL,
                    CONSTRAINT "PK_blog_post_tags" PRIMARY KEY (post_id, tag_slug),
                    CONSTRAINT "FK_blog_post_tags_blog_posts_post_id"
                        FOREIGN KEY (post_id)
                        REFERENCES blog.blog_posts (post_id)
                        ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS blog.blog_tags
                (
                    slug character varying(128) NOT NULL,
                    display_name character varying(256) NOT NULL,
                    description character varying(1024) NULL,
                    version integer NOT NULL,
                    created_utc timestamp with time zone NOT NULL,
                    updated_utc timestamp with time zone NOT NULL,
                    updated_by_actor_id character varying(256) NOT NULL,
                    CONSTRAINT "PK_blog_tags" PRIMARY KEY (slug)
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_blog_categories_name ON blog.blog_categories (name);
                CREATE INDEX IF NOT EXISTS ix_blog_post_publication_schedules_publish_utc ON blog.blog_post_publication_schedules (scheduled_publish_for_utc);
                CREATE INDEX IF NOT EXISTS ix_blog_post_publication_schedules_unpublish_utc ON blog.blog_post_publication_schedules (scheduled_unpublish_for_utc);
                CREATE UNIQUE INDEX IF NOT EXISTS ix_blog_post_revisions_post_id_revision_number ON blog.blog_post_revisions (post_id, revision_number);
                CREATE INDEX IF NOT EXISTS ix_blog_post_tags_tag_slug ON blog.blog_post_tags (tag_slug);
                CREATE INDEX IF NOT EXISTS ix_blog_tags_display_name ON blog.blog_tags (display_name);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blog_categories",
                schema: "blog");

            migrationBuilder.DropTable(
                name: "blog_post_publication_schedules",
                schema: "blog");

            migrationBuilder.DropTable(
                name: "blog_post_revisions",
                schema: "blog");

            migrationBuilder.DropTable(
                name: "blog_post_share_targets",
                schema: "blog");

            migrationBuilder.DropTable(
                name: "blog_post_tags",
                schema: "blog");

            migrationBuilder.DropTable(
                name: "blog_tags",
                schema: "blog");

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_sync_blog_posts_compatibility_columns ON blog.blog_posts;
                DROP FUNCTION IF EXISTS blog.sync_blog_posts_compatibility_columns();
                """);

            migrationBuilder.DropUniqueConstraint(
                name: "AK_blog_posts_post_id",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropIndex(
                name: "ux_blog_posts_slug",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropIndex(
                name: "ix_blog_posts_status_published_utc",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "body",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "category_slug",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "featured",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "revision_number",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "seo_description",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "seo_keywords",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "seo_title",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "view_count",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "published_by_actor_id",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "published_utc",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "updated_by_actor_id",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "updated_utc",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "created_utc",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "summary",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "title",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "slug",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.DropColumn(
                name: "post_id",
                schema: "blog",
                table: "blog_posts");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                schema: "blog",
                table: "blog_posts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                schema: "blog",
                table: "blog_posts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
