using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Infrastructure.Persistence;

public static class SharedRuntimePersistenceDefaults
{
    public const string ConnectionStringName = "BaselineDatabase";
    public const string SchemaName = "starter_runtime";
    public const string CommandIdempotencyTableName = "building_blocks_command_idempotency";
    public const string DataProtectionKeysTableName = "building_blocks_data_protection_keys";
    public const string DataProtectionApplicationName = "dotnet-modulith-baseline";
    public const string MissingConnectionStringMessage =
        "Shared runtime persistence requires ConnectionStrings:BaselineDatabase. Set the connection string and run DbMigrator before starting the host.";

    public static string GetRequiredConnectionString(IConfiguration? configuration)
    {
        var connectionString = configuration?.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(MissingConnectionStringMessage);
        }

        return connectionString;
    }
}
