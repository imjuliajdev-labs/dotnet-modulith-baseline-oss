namespace Platform.Infrastructure.Persistence;

public static class PlatformPersistenceDefaults
{
    public const string ConnectionStringName = "BaselineDatabase";

    public const string SchemaName = "platform";

    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public const string ModuleStatesTableName = "module_states";

    public const string ModuleStateChangesTableName = "module_state_changes";

    public const string AuditEventsTableName = "audit_events";
}
