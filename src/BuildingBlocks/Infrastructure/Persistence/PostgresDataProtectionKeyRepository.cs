using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace BuildingBlocks.Infrastructure.Persistence;

internal sealed class PostgresDataProtectionKeyRepository : IXmlRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresDataProtectionKeyRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var sql = $"""
            SELECT xml
            FROM {SharedRuntimePersistenceDefaults.SchemaName}.{SharedRuntimePersistenceDefaults.DataProtectionKeysTableName}
            ORDER BY id;
            """;

        var elements = new List<XElement>();

        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            elements.Add(XElement.Parse(reader.GetString(0)));
        }

        return elements;
    }

    public void StoreElement(XElement element, string? friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        var sql = $"""
            INSERT INTO {SharedRuntimePersistenceDefaults.SchemaName}.{SharedRuntimePersistenceDefaults.DataProtectionKeysTableName} (friendly_name, xml, created_utc)
            VALUES (@friendlyName, @xml, NOW());
            """;

        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("friendlyName", (object?)friendlyName ?? DBNull.Value);
        command.Parameters.AddWithValue("xml", element.ToString(SaveOptions.DisableFormatting));
        command.ExecuteNonQuery();
    }
}
