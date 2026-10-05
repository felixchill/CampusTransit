using CampusTransit.Data;
using Microsoft.EntityFrameworkCore;

namespace CampusTransit.Services;

/// <summary>
/// Stands in for the on-board GPS hardware: it advances each in-service shuttle along its route
/// and pushes the new telemetry over SignalR.
/// <para>
/// The loop keeps ticking, but when no client is connected to the transit hub a tick does
/// nothing at all — no database round trip and no broadcast — so idle time is effectively
/// free on hosting plans that meter CPU minutes per day. Polling rather than sleeping long
/// means the map starts moving within one tick of a client arriving.
/// </para>
/// </summary>
public class ShuttleSimulator(
    IServiceScopeFactory scopeFactory,
    TransitPresence presence,
    ILogger<ShuttleSimulator> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);

    private bool _wasActive;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var active = presence.HasClients;

            if (active != _wasActive)
            {
                _wasActive = active;
                logger.LogInformation(
                    active
                        ? "Shuttle simulation resumed: {Clients} client(s) connected."
                        : "Shuttle simulation paused: no connected clients.",
                    presence.Clients);
            }

            if (active)
            {
                await SimulateAsync(stoppingToken);
            }

            if (!await PauseAsync(Tick, stoppingToken))
            {
                break;
            }
        }
    }

    private async Task SimulateAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transit = scope.ServiceProvider.GetRequiredService<TransitService>();

            var shuttles = await db.Shuttles
                .Include(s => s.TransitRoute!).ThenInclude(r => r.Stops)
                .Where(s => s.Status == ShuttleStatus.InService && s.TransitRouteId != null)
                .ToListAsync(stoppingToken);

            foreach (var shuttle in shuttles)
            {
                var stops = shuttle.TransitRoute!.Stops.Count;
                if (stops == 0)
                {
                    continue;
                }

                shuttle.Progress = (shuttle.Progress + (Tick.TotalSeconds / Telemetry.LoopSeconds(stops))) % 1.0;
                shuttle.Occupancy = NextOccupancy(shuttle);

                var (latitude, longitude) = Telemetry.Position(shuttle);
                shuttle.Latitude = latitude;
                shuttle.Longitude = longitude;
                shuttle.LastUpdatedUtc = DateTime.UtcNow;

                await transit.BroadcastAsync(shuttle);
            }

            await db.SaveChangesAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Shuttle simulation tick failed; retrying on the next tick.");
        }
    }

    /// <summary>Waits, returning false when the host is shutting down.</summary>
    private static async Task<bool> PauseAsync(TimeSpan duration, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(duration, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Boarding and alighting drift that gently reverts to a busy-but-not-full vehicle.</summary>
    private static int NextOccupancy(Shuttle shuttle)
    {
        var target = shuttle.Capacity * 0.6;
        var towardTarget = Math.Sign(target - shuttle.Occupancy) * Random.Shared.Next(0, 3);
        var next = shuttle.Occupancy + towardTarget + Random.Shared.Next(-1, 2);

        return Math.Clamp(next, 1, shuttle.Capacity);
    }
}
