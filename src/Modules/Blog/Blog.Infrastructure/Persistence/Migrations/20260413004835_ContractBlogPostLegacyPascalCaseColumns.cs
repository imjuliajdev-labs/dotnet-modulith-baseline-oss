using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractBlogPostLegacyPascalCaseColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_sync_blog_posts_compatibility_columns ON blog.blog_posts;
                DROP FUNCTION IF EXISTS blog.sync_blog_posts_compatibility_columns();
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE blog.blog_post_publication_schedules
                    DROP CONSTRAINT IF EXISTS "FK_blog_post_publication_schedules_blog_posts_post_id";
                ALTER TABLE blog.blog_post_revisions
                    DROP CONSTRAINT IF EXISTS "FK_blog_post_revisions_blog_posts_post_id";
                ALTER TABLE blog.blog_post_share_targets
                    DROP CONSTRAINT IF EXISTS "FK_blog_post_share_targets_blog_posts_post_id";
                ALTER TABLE blog.blog_post_tags
                    DROP CONSTRAINT IF EXISTS "FK_blog_post_tags_blog_posts_post_id";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE blog.blog_posts DROP CONSTRAINT IF EXISTS "PK_blog_posts";
                ALTER TABLE blog.blog_posts DROP CONSTRAINT IF EXISTS "AK_blog_posts_post_id";
                ALTER TABLE blog.blog_posts ADD CONSTRAINT "PK_blog_posts" PRIMARY KEY (post_id);
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE blog.blog_post_publication_schedules
                    ADD CONSTRAINT "FK_blog_post_publication_schedules_blog_posts_post_id"
                    FOREIGN KEY (post_id) REFERENCES blog.blog_posts (post_id) ON DELETE CASCADE;
                ALTER TABLE blog.blog_post_revisions
                    ADD CONSTRAINT "FK_blog_post_revisions_blog_posts_post_id"
                    FOREIGN KEY (post_id) REFERENCES blog.blog_posts (post_id) ON DELETE CASCADE;
                ALTER TABLE blog.blog_post_share_targets
                    ADD CONSTRAINT "FK_blog_post_share_targets_blog_posts_post_id"
                    FOREIGN KEY (post_id) REFERENCES blog.blog_posts (post_id) ON DELETE CASCADE;
                ALTER TABLE blog.blog_post_tags
                    ADD CONSTRAINT "FK_blog_post_tags_blog_posts_post_id"
                    FOREIGN KEY (post_id) REFERENCES blog.blog_posts (post_id) ON DELETE CASCADE;
                """);

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS blog."IX_blog_posts_Slug";
                DROP INDEX IF EXISTS blog."IX_blog_posts_Status_PublishedUtc";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE blog.blog_posts
                    DROP COLUMN IF EXISTS "PostId",
                    DROP COLUMN IF EXISTS "Slug",
                    DROP COLUMN IF EXISTS "Title",
                    DROP COLUMN IF EXISTS "Summary",
                    DROP COLUMN IF EXISTS "Status",
                    DROP COLUMN IF EXISTS "Version",
                    DROP COLUMN IF EXISTS "CreatedUtc",
                    DROP COLUMN IF EXISTS "UpdatedUtc",
                    DROP COLUMN IF EXISTS "UpdatedByActorId",
                    DROP COLUMN IF EXISTS "PublishedUtc",
                    DROP COLUMN IF EXISTS "PublishedByActorId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException(
                "ContractBlogPostLegacyPascalCaseColumns drops the legacy compatibility columns, trigger, and function. " +
                "Rolling this back in-place would require restoring the ExpandBlogEditorialModel sync trigger and " +
                "repopulating the dropped columns from snake_case values. Follow the durable rollback assessment at " +
                "docs/operations/rollback/blog-contract-legacy-pascalcase-columns.md and restore from backup if you need the old shape.");
        }
    }
}
