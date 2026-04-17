namespace Architecture.Tests.ModuleCoverage.Blog.Configuration;

public sealed class BlogConfigurationGovernanceTests
{
    [Xunit.Fact]
    public void InfrastructureWiringBindsTypedOptionsAndValidatesOnStart()
    {
        var source = RepositoryFiles.ReadAllText("src/Modules/Blog/Blog.Infrastructure/BlogInfrastructureServiceCollectionExtensions.cs");
        var dbContextSource = RepositoryFiles.ReadAllText("src/Modules/Blog/Blog.Infrastructure/Persistence/BlogPersistenceDbContext.cs");
        var mappingSource = RepositoryFiles.ReadAllText("src/Modules/Blog/Blog.Infrastructure/Persistence/BlogPostEntityTypeConfiguration.cs");
        var optionsFileSource = RepositoryFiles.ReadAllText("src/Modules/Blog/Blog.Infrastructure/Configuration/BlogInfrastructureOptions.cs");
        var optionsSource = RepositoryFiles.ReadAllText("src/Modules/Blog/Blog.Infrastructure/Persistence/BlogPersistenceOptionsExtensions.cs");

        Xunit.Assert.Contains("AddOptions<BlogInfrastructureOptions>()", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("BlogInfrastructureOptions.SectionName", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("ValidateOnStart()", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("AddDbContextFactory<BlogPersistenceDbContext>", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("UseBlogPersistence(connectionString)", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("TryAddSingleton<IBlogPostStore, BlogPostStore>()", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("TryAddSingleton<IBlogSettingsStore, BlogSettingsStore>()", source, StringComparison.Ordinal);
        Xunit.Assert.DoesNotContain("InMemoryBlogSettingsStore", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("IBlogPublishedPostQueryService", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("HasDefaultSchema(BlogPersistenceDefaults.SchemaName)", dbContextSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("ApplyConfiguration(new BlogPostEntityTypeConfiguration())", dbContextSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("IEntityTypeConfiguration<BlogPostRecord>", mappingSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("IsConcurrencyToken()", mappingSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("HasIndex(record => record.Slug).IsUnique()", mappingSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("OperatorManagedSettings", optionsFileSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("Featured posts preview limit", optionsFileSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("MigrationsHistoryTable(", optionsSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("SettingsTableName", dbContextSource, StringComparison.Ordinal);
    }

    [Xunit.Fact]
    public void RuntimeSettingsCommandsRemainRoleGuardedAndRecentAuthProtected()
    {
        var source = RepositoryFiles.ReadAllText("src/Modules/Blog/Blog.Application/Settings/BlogSettingsContracts.cs");

        Xunit.Assert.Contains("IAuthorizeRequest", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("IRequireRecentAuthentication", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("RoleRequirement.Admin", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("blog.settings.update", source, StringComparison.Ordinal);
    }
}
