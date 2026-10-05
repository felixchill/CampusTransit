using CampusTransit.Data;
using CampusTransit.Hubs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CampusTransit.Services;

/// <summary>Application service that owns all transit reads and writes.</summary>
public class TransitService(AppDbContext db, IPasswordHasher<AppUser> hasher, IHubContext<TransitHub> hub)
{
    // ---------------------------------------------------------------- reads

    public Task<List<TransitRoute>> GetRoutesAsync() =>
        db.Routes.Include(r => r.Stops).OrderBy(r => r.Code).AsNoTracking().ToListAsync();

    public Task<List<Shuttle>> GetShuttlesAsync() =>
        db.Shuttles
            .Include(s => s.TransitRoute!).ThenInclude(r => r.Stops)
            .Include(s => s.Driver)
            .OrderBy(s => s.Code)
            .AsNoTracking()
            .ToListAsync();

    public Task<List<Shuttle>> GetShuttlesForDriverAsync(int driverId) =>
        db.Shuttles
            .Include(s => s.TransitRoute!).ThenInclude(r => r.Stops)
            .Include(s => s.Driver)
            .Where(s => s.DriverId == driverId)
            .OrderBy(s => s.Code)
            .ToListAsync();

    public Task<List<Reservation>> GetReservationsAsync(int userId) =>
        db.Reservations
            .Include(r => r.TransitRoute)
            .Include(r => r.Stop)
            .Include(r => r.Shuttle)
            .Where(r => r.AppUserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync();

    public Task<List<Reservation>> GetAllReservationsAsync(int take = 60) =>
        db.Reservations
            .Include(r => r.AppUser)
            .Include(r => r.TransitRoute)
            .Include(r => r.Stop)
            .Include(r => r.Shuttle)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(take)
            .AsNoTracking()
            .ToListAsync();

    public Task<List<AppUser>> GetUsersAsync(UserRole role) =>
        db.Users.Where(u => u.Role == role).OrderBy(u => u.FullName).AsNoTracking().ToListAsync();

    public Task<List<ServiceReport>> GetReportsAsync(int take = 25) =>
        db.ServiceReports
            .Include(r => r.Shuttle)
            .Include(r => r.Driver)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(take)
            .AsNoTracking()
            .ToListAsync();

    public async Task<AppUser?> AuthenticateAsync(string email, string password)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower());
        if (user is null)
        {
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : user;
    }

    public Task<AppUser?> GetUserAsync(int id) => db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);

    public async Task<List<ShuttleUpdate>> GetLiveAsync(string? routeCode = null)
    {
        var shuttles = await GetShuttlesAsync();
        return shuttles
            .Where(s => routeCode is null || s.TransitRoute?.Code == routeCode)
            .Select(Telemetry.ToUpdate)
            .ToList();
    }

    // ------------------------------------------------------------- commands

    /// <summary>Books seats on a shuttle and tells every connected client about it.</summary>
    public async Task<(bool Ok, string Message, Reservation? Reservation)> ReserveAsync(
        int userId, int routeId, int shuttleId, int stopId, int seats)
    {
        if (seats is < 1 or > 6)
        {
            return (false, "You can reserve between one and six seats.", null);
        }

        var shuttle = await db.Shuttles.Include(s => s.TransitRoute).FirstOrDefaultAsync(s => s.Id == shuttleId);
        if (shuttle is null)
        {
            return (false, "That shuttle is no longer in service.", null);
        }

        var available = shuttle.Capacity - shuttle.Occupancy;
        if (available < seats)
        {
            return (false, $"Only {Math.Max(0, available)} seat(s) left on {shuttle.Code}.", null);
        }

        var alreadyBooked = await db.Reservations.CountAsync(r =>
            r.AppUserId == userId && r.ShuttleId == shuttleId && r.Status == ReservationStatus.Confirmed);

        if (alreadyBooked + seats > 6)
        {
            return (false, "You already hold the maximum number of seats on this shuttle.", null);
        }

        shuttle.Occupancy += seats;

        var reservation = new Reservation
        {
            Reference = await NextReferenceAsync(),
            AppUserId = userId,
            TransitRouteId = routeId,
            ShuttleId = shuttleId,
            StopId = stopId,
            Seats = seats,
            Status = ReservationStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow,
            TravelDateUtc = DbSeeder.Today()
        };

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        await BroadcastAsync(shuttle);

        var stopName = await db.Stops.Where(s => s.Id == stopId).Select(s => s.Name).FirstOrDefaultAsync() ?? "Stop";
        await hub.Clients.All.SendAsync("ReservationChanged", new ReservationUpdate(
            reservation.Reference,
            shuttle.TransitRoute?.Name ?? "Route",
            stopName,
            reservation.Seats,
            reservation.Status.ToString(),
            DateTime.UtcNow));

        return (true, $"Seat reserved. Reference {reservation.Reference}.", reservation);
    }

    public async Task<bool> CancelAsync(int reservationId, int userId, bool isStaff)
    {
        var reservation = await db.Reservations
            .Include(r => r.Shuttle)
            .Include(r => r.TransitRoute)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation is null || (!isStaff && reservation.AppUserId != userId))
        {
            return false;
        }

        if (reservation.Status != ReservationStatus.Confirmed)
        {
            return false;
        }

        reservation.Status = ReservationStatus.Cancelled;

        if (reservation.Shuttle is not null)
        {
            reservation.Shuttle.Occupancy = Math.Max(0, reservation.Shuttle.Occupancy - reservation.Seats);
            await BroadcastAsync(reservation.Shuttle);
        }

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateShuttleAsync(int shuttleId, int? occupancy, ShuttleStatus? status, int driverId, bool isAdmin)
    {
        var shuttle = await db.Shuttles.Include(s => s.TransitRoute!).ThenInclude(r => r.Stops).FirstOrDefaultAsync(s => s.Id == shuttleId);
        if (shuttle is null || (!isAdmin && shuttle.DriverId != driverId))
        {
            return false;
        }

        if (occupancy is not null)
        {
            shuttle.Occupancy = Math.Clamp(occupancy.Value, 0, shuttle.Capacity);
        }

        if (status is not null)
        {
            shuttle.Status = status.Value;
        }

        await db.SaveChangesAsync();
        await BroadcastAsync(shuttle);
        return true;
    }

    public async Task<bool> SetRouteActiveAsync(int routeId, bool active)
    {
        var route = await db.Routes.FindAsync(routeId);
        if (route is null)
        {
            return false;
        }

        route.IsActive = active;
        await db.SaveChangesAsync();
        await hub.Clients.All.SendAsync("RouteChanged", new { route.Id, route.Code, route.IsActive });
        return true;
    }

    public async Task<ServiceReport> ReportAsync(int shuttleId, int driverId, ReportKind kind, string message)
    {
        var report = new ServiceReport
        {
            ShuttleId = shuttleId,
            DriverId = driverId,
            Kind = kind,
            Message = message.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.ServiceReports.Add(report);
        await db.SaveChangesAsync();

        var shuttle = await db.Shuttles.FindAsync(shuttleId);
        var driver = await db.Users.FindAsync(driverId);

        await hub.Clients.All.SendAsync("ReportRaised", new ReportUpdate(
            report.Id,
            kind.ToString(),
            report.Message,
            shuttle?.Code ?? "—",
            driver?.FullName,
            report.CreatedAtUtc));

        return report;
    }

    public async Task BroadcastAsync(Shuttle shuttle)
    {
        if (shuttle.TransitRoute is null)
        {
            await db.Entry(shuttle).Reference(s => s.TransitRoute).LoadAsync();
        }

        var update = Telemetry.ToUpdate(shuttle);
        string[] feeds = update.RouteCode is null ? [TransitHub.AllGroup] : TransitHub.Feed(update.RouteCode);

        await hub.Clients.Groups(feeds).SendAsync("ShuttleUpdated", update);
    }

    private async Task<string> NextReferenceAsync()
    {
        var count = await db.Reservations.CountAsync();
        var reference = $"CT-{DateTime.UtcNow:yyMMdd}-{1000 + count}";

        while (await db.Reservations.AnyAsync(r => r.Reference == reference))
        {
            count++;
            reference = $"CT-{DateTime.UtcNow:yyMMdd}-{1000 + count}";
        }

        return reference;
    }
}
