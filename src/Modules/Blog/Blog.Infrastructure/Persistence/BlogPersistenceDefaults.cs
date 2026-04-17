namespace Blog.Infrastructure.Persistence;

public static class BlogPersistenceDefaults
{
    public const string ConnectionStringName = "BaselineDatabase";

    public const string SchemaName = "blog";

    public const string PostsTableName = "blog_posts";

    public const string CategoriesTableName = "blog_categories";

    public const string TagsTableName = "blog_tags";

    public const string PostTagsTableName = "blog_post_tags";

    public const string PostShareTargetsTableName = "blog_post_share_targets";

    public const string PostRevisionsTableName = "blog_post_revisions";

    public const string PostPublicationSchedulesTableName = "blog_post_publication_schedules";

    public const string SettingsTableName = "blog_module_settings";

    public const string SettingsRowKey = "default";

    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";
}
