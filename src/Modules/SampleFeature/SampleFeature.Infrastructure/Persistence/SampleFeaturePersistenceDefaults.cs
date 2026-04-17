namespace SampleFeature.Infrastructure.Persistence;

internal static class SampleFeaturePersistenceDefaults
{
    public const string ConnectionStringName = "BaselineDatabase";
    public const string SchemaName = "sample_feature";
    public const string AnnouncementsTableName = "announcements";
    public const string ScheduledAnnouncementsTableName = "scheduled_announcements";
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";
}
