using Microsoft.AspNetCore.SignalR;

namespace BuildingBlocks.Infrastructure.Realtime;

public interface IBrowserRealtimeNotifier
{
    Task PublishToRolesAsync(
        IEnumerable<string> roles,
        string channel,
        object payload,
        CancellationToken cancellationToken);
}

internal sealed class SignalRBrowserRealtimeNotifier : IBrowserRealtimeNotifier
{
    private readonly IHubContext<BrowserRealtimeHub> _hubContext;

    public SignalRBrowserRealtimeNotifier(IHubContext<BrowserRealtimeHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public async Task PublishToRolesAsync(
        IEnumerable<string> roles,
        string channel,
        object payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(payload);

        var groups = roles
            .Where(static role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .Select(BrowserRealtimeGroupNames.Role)
            .ToArray();

        if (groups.Length == 0)
        {
            return;
        }

        await _hubContext.Clients.Groups(groups)
            .SendAsync(BrowserRealtimeMethods.Message, channel, payload, cancellationToken);
    }
}
