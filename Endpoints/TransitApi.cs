using System.Security.Claims;
using CampusTransit.Data;
using CampusTransit.Services;
using Microsoft.EntityFrameworkCore;

namespace CampusTransit.Endpoints;

/// <summary>
/// JSON API surface. The Blazor UI talks to the in-process service directly, while these
/// endpoints expose the same data to external clients and integration tests.
/// </summary>
public static class TransitApi
{
    public static IEndpointRouteBuilder MapTransitApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("CampusTransit");

        api.MapGet("/routes", async (TransitService transit) =>
        {
            var routes = await transit.GetRoutesAsync();
            return Results.Ok(routes.Select(r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.Description,
                r.ColorHex,
                r.HeadwayMinutes,
                r.IsActive,
                Stops = r.Stops.OrderBy(s => s.Sequence).Select(s => new { s.Id, s.Name, s.Latitude, s.Longitude, s.Sequence })
            }));
        });

        api.MapGet("/shuttles", async (TransitService transit) =>
        {
            var shuttles = await transit.GetShuttlesAsync();
            return Results.Ok(shuttles.Select(Telemetry.ToUpdate));
        });

        api.MapGet("/shuttles/live", async (string? route, TransitService transit) =>
            Results.Ok(await transit.GetLiveAsync(route)));

        api.MapGet("/reservations", async (ClaimsPrincipal user, TransitService transit) =>
        {
            var userId = user.GetUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var reservations = await transit.GetReservationsAsync(userId.Value);
            return Results.Ok(reservations.Select(ToDto));
        }).RequireAuthorization();

        api.MapPost("/reservations", async (ReservationRequest request, ClaimsPrincipal user, TransitService transit) =>
        {
            var userId = user.GetUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var (ok, message, reservation) = await transit.ReserveAsync(userId.Value, request.RouteId, request.ShuttleId, request.StopId, request.Seats);
            return ok ? Results.Created($"/api/reservations/{reservation!.Id}", new { message, reservation.Reference }) : Results.BadRequest(new { message });
        }).RequireAuthorization();

        api.MapDelete("/reservations/{id:int}", async (int id, ClaimsPrincipal user, TransitService transit) =>
        {
            var userId = user.GetUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var isStaff = user.IsInRole(nameof(UserRole.Admin)) || user.IsInRole(nameof(UserRole.Driver));
            var cancelled = await transit.CancelAsync(id, userId.Value, isStaff);
            return cancelled ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization();

        api.MapGet("/reports", async (TransitService transit) =>
        {
            var reports = await transit.GetReportsAsync();
            return Results.Ok(reports.Select(r => new
            {
                r.Id,
                Kind = r.Kind.ToString(),
                r.Message,
                Shuttle = r.Shuttle?.Code,
                Driver = r.Driver?.FullName,
                r.CreatedAtUtc
            }));
        }).RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Driver), nameof(UserRole.Admin)));

        api.MapPost("/reports", async (ReportRequest request, ClaimsPrincipal user, TransitService transit, AppDbContext db) =>
        {
            var userId = user.GetUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            if (!Enum.TryParse<ReportKind>(request.Kind, ignoreCase: true, out var kind))
            {
                return Results.BadRequest(new { message = "Unknown report kind." });
            }

            var report = await transit.ReportAsync(request.ShuttleId, userId.Value, kind, request.Message);
            var shuttleCode = await db.Shuttles.Where(s => s.Id == report.ShuttleId).Select(s => s.Code).FirstOrDefaultAsync();

            return Results.Created($"/api/reports/{report.Id}", new
            {
                report.Id,
                Kind = report.Kind.ToString(),
                report.Message,
                Shuttle = shuttleCode,
                report.CreatedAtUtc
            });
        }).RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Driver), nameof(UserRole.Admin)));

        api.MapGet("/stats", async (AppDbContext db) => Results.Ok(new
        {
            Routes = await db.Routes.CountAsync(r => r.IsActive),
            Shuttles = await db.Shuttles.CountAsync(),
            InService = await db.Shuttles.CountAsync(s => s.Status == ShuttleStatus.InService),
            SeatsAvailable = await db.Shuttles.SumAsync(s => s.Capacity - s.Occupancy),
            Reservations = await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Confirmed)
        }));

        return app;
    }

    private static object ToDto(Reservation reservation) => new
    {
        reservation.Id,
        reservation.Reference,
        Route = reservation.TransitRoute?.Name,
        Shuttle = reservation.Shuttle?.Code,
        Stop = reservation.Stop?.Name,
        reservation.Seats,
        Status = reservation.Status.ToString(),
        reservation.CreatedAtUtc
    };

    private static int? GetUserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public record ReservationRequest(int RouteId, int ShuttleId, int StopId, int Seats);

    public record ReportRequest(int ShuttleId, string Kind, string Message);
}
