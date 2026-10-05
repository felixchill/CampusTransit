namespace CampusTransit.Services;

/// <summary>Telemetry pushed to clients every simulation tick.</summary>
public record ShuttleUpdate(
    int ShuttleId,
    string Code,
    string? RouteCode,
    string? RouteName,
    string Color,
    double Latitude,
    double Longitude,
    int Occupancy,
    int Capacity,
    int SeatsAvailable,
    string Status,
    string? NextStop,
    int EtaMinutes,
    DateTime UpdatedAtUtc);

public record ReportUpdate(int Id, string Kind, string Message, string ShuttleCode, string? Driver, DateTime CreatedAtUtc);

public record ReservationUpdate(string Reference, string RouteName, string StopName, int Seats, string Status, DateTime CreatedAtUtc);
