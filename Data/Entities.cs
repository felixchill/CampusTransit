namespace CampusTransit.Data;

public enum UserRole
{
    Student,
    Faculty,
    Driver,
    Admin
}

public enum ShuttleStatus
{
    Idle,
    InService,
    OutOfService
}

public enum ReservationStatus
{
    Confirmed,
    Cancelled,
    Completed
}

public enum ReportKind
{
    Delay,
    Capacity,
    Maintenance,
    Incident
}

public class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string? Affiliation { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}

public class TransitRoute
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#4f46e5";
    public int HeadwayMinutes { get; set; } = 10;
    public bool IsActive { get; set; } = true;

    public ICollection<Stop> Stops { get; set; } = new List<Stop>();
    public ICollection<Shuttle> Shuttles { get; set; } = new List<Shuttle>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}

public class Stop
{
    public int Id { get; set; }
    public int TransitRouteId { get; set; }
    public TransitRoute? TransitRoute { get; set; }

    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Sequence { get; set; }
}

public class Shuttle
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Capacity { get; set; } = 30;
    public int Occupancy { get; set; }
    public ShuttleStatus Status { get; set; } = ShuttleStatus.Idle;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Progress { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    public int? TransitRouteId { get; set; }
    public TransitRoute? TransitRoute { get; set; }

    public int? DriverId { get; set; }
    public AppUser? Driver { get; set; }

    public int SeatsAvailable => Math.Max(0, Capacity - Occupancy);
}

public class Reservation
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;

    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    public int TransitRouteId { get; set; }
    public TransitRoute? TransitRoute { get; set; }

    public int? ShuttleId { get; set; }
    public Shuttle? Shuttle { get; set; }

    public int StopId { get; set; }
    public Stop? Stop { get; set; }

    public int Seats { get; set; } = 1;
    public ReservationStatus Status { get; set; } = ReservationStatus.Confirmed;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Midnight of the travel day as an explicit UTC instant, for PostgreSQL compatibility.</summary>
    public DateTime TravelDateUtc { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
}

/// <summary>Operational report raised by a driver from the console.</summary>
public class ServiceReport
{
    public int Id { get; set; }
    public ReportKind Kind { get; set; }
    public string Message { get; set; } = string.Empty;

    public int ShuttleId { get; set; }
    public Shuttle? Shuttle { get; set; }

    public int? DriverId { get; set; }
    public AppUser? Driver { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
