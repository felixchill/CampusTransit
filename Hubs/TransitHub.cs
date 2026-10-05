using CampusTransit.Services;
using Microsoft.AspNetCore.SignalR;

namespace CampusTransit.Hubs;

/// <summary>
/// Streams live shuttle telemetry, seat counts and service reports to every connected client.
/// </summary>
public class TransitHub(TransitPresence presence) : Hub
{
    public const string AllGroup = "all";

    public override async Task OnConnectedAsync()
    {
        presence.Join();
        await Groups.AddToGroupAsync(Context.ConnectionId, AllGroup);
        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        presence.Leave();
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Subscribe the caller to a single route so it only receives relevant telemetry.</summary>
    public Task FollowRoute(string routeCode) => Groups.AddToGroupAsync(Context.ConnectionId, RouteGroup(routeCode));

    public Task UnfollowRoute(string routeCode) => Groups.RemoveFromGroupAsync(Context.ConnectionId, RouteGroup(routeCode));

    public static string RouteGroup(string routeCode) => $"route:{routeCode.ToUpperInvariant()}";

    /// <summary>Target the shared feed and one route feed without delivering a message twice.</summary>
    public static string[] Feed(string routeCode) => [AllGroup, RouteGroup(routeCode)];
}
