using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace BuildingBlocks.Infrastructure.Persistence;

public static class PostgresAdvisoryLock
{
    public static Task<IAsyncDisposable> AcquireAsync(
        NpgsqlDataSource dataSource,
        string lockName,
        CancellationToken cancellationToken)
    {
        return AcquireAsync(dataSource, lockName, shared: false, ownsDataSource: false, cancellationToken);
    }

    public static Task<IAsyncDisposable> AcquireAsync(
        string connectionString,
        string lockName,
        CancellationToken cancellationToken)
    {
        return AcquireAsync(CreateOwnedDataSource(connectionString), lockName, shared: false, ownsDataSource: true, cancellationToken);
    }

    public static Task<IAsyncDisposable> AcquireSharedAsync(
        NpgsqlDataSource dataSource,
        string lockName,
        CancellationToken cancellationToken)
    {
        return AcquireAsync(dataSource, lockName, shared: true, ownsDataSource: false, cancellationToken);
    }

    public static Task<IAsyncDisposable> AcquireSharedAsync(
        string connectionString,
        string lockName,
        CancellationToken cancellationToken)
    {
        return AcquireAsync(CreateOwnedDataSource(connectionString), lockName, shared: true, ownsDataSource: true, cancellationToken);
    }

    private static NpgsqlDataSource CreateOwnedDataSource(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Enlist = false
        };

        return new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString).Build();
    }

    private static async Task<IAsyncDisposable> AcquireAsync(
        NpgsqlDataSource dataSource,
        string lockName,
        bool shared,
        bool ownsDataSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockName);

        var lockKey = ComputeLockKey(lockName);
        NpgsqlConnection? connection = null;

        try
        {
            connection = await dataSource.OpenConnectionAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = shared
                ? "select pg_advisory_lock_shared(@lock_key);"
                : "select pg_advisory_lock(@lock_key);";
            command.Parameters.AddWithValue("lock_key", lockKey);
            await command.ExecuteNonQueryAsync(cancellationToken);

            return new Releaser(connection, lockKey, shared, ownsDataSource ? dataSource : null);
        }
        catch
        {
            if (connection is not null)
            {
                await connection.DisposeAsync();
            }

            if (ownsDataSource)
            {
                await dataSource.DisposeAsync();
            }

            throw;
        }
    }

    private static long ComputeLockKey(string lockName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(lockName));
        return BitConverter.ToInt64(hash, 0);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly long _lockKey;
        private readonly bool _shared;
        private readonly NpgsqlDataSource? _ownedDataSource;

        public Releaser(NpgsqlConnection connection, long lockKey, bool shared, NpgsqlDataSource? ownedDataSource)
        {
            _connection = connection;
            _lockKey = lockKey;
            _shared = shared;
            _ownedDataSource = ownedDataSource;
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                await using var command = _connection.CreateCommand();
                command.CommandText = _shared
                    ? "select pg_advisory_unlock_shared(@lock_key);"
                    : "select pg_advisory_unlock(@lock_key);";
                command.Parameters.AddWithValue("lock_key", _lockKey);
                await command.ExecuteNonQueryAsync();
            }

            await _connection.DisposeAsync();

            if (_ownedDataSource is not null)
            {
                await _ownedDataSource.DisposeAsync();
            }
        }
    }
}
