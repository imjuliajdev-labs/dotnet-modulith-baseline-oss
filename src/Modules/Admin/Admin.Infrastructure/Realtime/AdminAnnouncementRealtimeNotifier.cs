using Admin.Application.Realtime;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Infrastructure.Realtime;

namespace Admin.Infrastructure.Realtime;

internal sealed class AdminAnnouncementRealtimeNotifier : IAdminAnnouncementRealtimeNotifier
{
    private readonly IBrowserRealtimeNotifier _browserRealtimeNotifier;

    public AdminAnnouncementRealtimeNotifier(IBrowserRealtimeNotifier browserRealtimeNotifier)
    {
        _browserRealtimeNotifier = browserRealtimeNotifier ?? throw new ArgumentNullException(nameof(browserRealtimeNotifier));
    }

    public Task PublishProjectedAsync(AdminAnnouncementProjectedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return _browserRealtimeNotifier.PublishToRolesAsync(
            [IdentityRoles.Admin],
            AdminRealtimeChannels.AnnouncementProjected,
            notification,
            cancellationToken);
    }
}
