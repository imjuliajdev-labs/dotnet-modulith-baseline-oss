using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BuildingBlocks.Infrastructure.Realtime;

[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public sealed class BrowserRealtimeHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var roles = Context.User?.Claims
            .Where(static claim => string.Equals(claim.Type, ClaimTypes.Role, StringComparison.Ordinal))
            .Select(static claim => claim.Value)
            .Where(static role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? [];

        foreach (var role in roles)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, BrowserRealtimeGroupNames.Role(role));
        }

        await base.OnConnectedAsync();
    }
}

public static class BrowserRealtimeDefaults
{
    public const string Path = "/realtime/browser";
}

public static class BrowserRealtimeMethods
{
    public const string Message = "message";
}

public static class BrowserRealtimeGroupNames
{
    public static string Role(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return $"role:{role.Trim()}";
    }
}
