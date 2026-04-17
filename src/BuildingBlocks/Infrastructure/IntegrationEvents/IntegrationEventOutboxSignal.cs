using Microsoft.Extensions.Logging;
using Npgsql;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

public sealed record IntegrationEventOutboxSignalHolder(IntegrationEventOutboxSignal? Signal);

public sealed class IntegrationEventOutboxSignal : IAsyncDisposable
{
    public const string ChannelName = "outbox_dispatch";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<IntegrationEventOutboxSignal> _logger;
    private NpgsqlConnection? _listenerConnection;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private CancellationTokenSource? _listenerCts;

    public IntegrationEventOutboxSignal(NpgsqlDataSource dataSource, ILogger<IntegrationEventOutboxSignal> logger)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken)
    {
        _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenerConnection = await _dataSource.OpenConnectionAsync(cancellationToken);

        _listenerConnection.Notification += OnNotification;
        await using var command = new NpgsqlCommand($"LISTEN {ChannelName}", _listenerConnection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        _ = Task.Run(() => WaitForNotificationsAsync(_listenerCts.Token), cancellationToken);
    }

    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return await _signal.WaitAsync(timeout, cancellationToken);
    }

    public static async Task NotifyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand($"NOTIFY {ChannelName}", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        if (_signal.CurrentCount == 0)
        {
            _signal.Release();
        }
    }

    private async Task WaitForNotificationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _listenerConnection is not null)
            {
                await _listenerConnection.WaitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Outbox LISTEN/NOTIFY listener stopped unexpectedly.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_listenerCts is not null)
        {
            await _listenerCts.CancelAsync();
            _listenerCts.Dispose();
        }

        if (_listenerConnection is not null)
        {
            await _listenerConnection.DisposeAsync();
        }

        _signal.Dispose();
    }
}
