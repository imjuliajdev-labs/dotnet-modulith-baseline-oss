using BuildingBlocks.Application.Dispatching;

namespace Architecture.Tests;

public sealed class DispatcherIdempotencyGuardrailTests
{
    [Fact]
    public void CommandIdempotencyContextCarriesCallerIdentity()
    {
        var callerIdentityProperty = typeof(CommandIdempotencyContext).GetProperty(nameof(CommandIdempotencyContext.CallerIdentity));

        Assert.NotNull(callerIdentityProperty);
        Assert.Equal(typeof(string), Nullable.GetUnderlyingType(callerIdentityProperty!.PropertyType) ?? callerIdentityProperty.PropertyType);
    }

    [Fact]
    public void SharedRuntimeMigrationOwnsTheCommandIdempotencyTable()
    {
        var source = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Persistence/SharedRuntimePersistenceDatabaseMigration.cs");

        Assert.Contains("SharedRuntimePersistenceDefaults.CommandIdempotencyTableName", source, StringComparison.Ordinal);
        Assert.Contains("caller_identity", source, StringComparison.Ordinal);
        Assert.Contains("expires_utc", source, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN legacy.is_completed THEN 'completed' ELSE 'abandoned' END", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeIdempotencyStoreDoesNotCreateTablesLazily()
    {
        var source = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/Dispatching/PostgresCommandIdempotencyStore.cs");

        Assert.DoesNotContain("CREATE TABLE", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SharedRuntimePersistenceDefaults.SchemaName", source, StringComparison.Ordinal);
        Assert.Contains("caller_identity", source, StringComparison.Ordinal);
        Assert.Contains("status = 'running'", source, StringComparison.Ordinal);
        Assert.Contains("status = 'abandoned'", source, StringComparison.Ordinal);
        Assert.Contains("status = 'completed'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildingBlocksInfrastructureRegistersCommandIdempotencyRetentionCleanup()
    {
        var source = RepositoryFiles.ReadAllText("src/BuildingBlocks/Infrastructure/ServiceCollectionExtensions.cs");

        Assert.Contains("AddOptions<CommandIdempotencyRetentionOptions>()", source, StringComparison.Ordinal);
        Assert.Contains("CommandIdempotencyRetentionHostedService", source, StringComparison.Ordinal);
    }
}
