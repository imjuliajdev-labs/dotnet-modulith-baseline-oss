using Admin.PublicContracts.Queries;
using BuildingBlocks.Infrastructure.Persistence;
using Npgsql;

namespace Admin.Infrastructure.Inbox;

internal sealed class AdminAnnouncementReader : IAdminAnnouncementQueryService
{
    private readonly NpgsqlDataSource _dataSource;

    public AdminAnnouncementReader(IPostgresDataSourceResolver dataSourceResolver)
    {
        ArgumentNullException.ThrowIfNull(dataSourceResolver);

        _dataSource = dataSourceResolver.GetRequiredDataSource(AdminPersistenceDefaults.ConnectionStringName);
    }

    public async ValueTask<AdminAnnouncementReadModel?> GetAsync(Guid announcementId, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT announcement_id, title, body, published_utc, published_by_actor_id, source_module_key, source_reference
            FROM {AdminPersistenceDefaults.SchemaName}.{AdminPersistenceDefaults.AnnouncementsTableName}
            WHERE announcement_id = @announcementId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("announcementId", announcementId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminAnnouncementReadModel(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetString(4))
        {
            SourceModuleKey = reader.GetString(5),
            SourceReference = reader.IsDBNull(6) ? null : reader.GetString(6)
        };
    }

    public async ValueTask<IReadOnlyCollection<AdminAnnouncementReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT announcement_id, title, body, published_utc, published_by_actor_id, source_module_key, source_reference
            FROM {AdminPersistenceDefaults.SchemaName}.{AdminPersistenceDefaults.AnnouncementsTableName}
            ORDER BY published_utc DESC, announcement_id DESC
            LIMIT @limit;
            """;

        var announcements = new List<AdminAnnouncementReadModel>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            announcements.Add(new AdminAnnouncementReadModel(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetString(4))
            {
                SourceModuleKey = reader.GetString(5),
                SourceReference = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return announcements;
    }
}
