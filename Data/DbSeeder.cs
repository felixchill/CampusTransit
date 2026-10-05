using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CampusTransit.Data;

/// <summary>
/// Creates the SQLite schema and loads the demonstration network for the
/// University of Ghana, Legon campus.
/// </summary>
public static class DbSeeder
{
    public const string DefaultPasswordSuffix = "@123";

    /// <summary>
    /// Marks the currently shipped demonstration data set. When the stored data does not
    /// carry this route code it is replaced, so a changed seed always takes effect.
    /// </summary>
    public const string DataSetTag = "BAL";

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher<AppUser> hasher)
    {
        await db.Database.EnsureCreatedAsync();

        if (await db.Routes.AnyAsync(r => r.Code == DataSetTag))
        {
            return;
        }

        // Either a fresh database or an older demonstration data set: start clean.
        await db.Reservations.ExecuteDeleteAsync();
        await db.ServiceReports.ExecuteDeleteAsync();
        await db.Shuttles.ExecuteDeleteAsync();
        await db.Stops.ExecuteDeleteAsync();
        await db.Routes.ExecuteDeleteAsync();
        await db.Users.ExecuteDeleteAsync();

        var users = new[]
        {
            NewUser(hasher, "student@st.ug.edu.gh", "Yaw Owusu", UserRole.Student, "BSc Computer Science, Level 300", "Student"),
            NewUser(hasher, "faculty@ug.edu.gh", "Dr. Efua Sarpong", UserRole.Faculty, "Department of Physics", "Faculty"),
            NewUser(hasher, "driver@ug.edu.gh", "Kwame Mensah", UserRole.Driver, "Transport Section, Shift A", "Driver"),
            NewUser(hasher, "driver2@ug.edu.gh", "Ama Boateng", UserRole.Driver, "Transport Section, Shift B", "Driver"),
            NewUser(hasher, "admin@ug.edu.gh", "Nana Adjei", UserRole.Admin, "Transportation & Logistics Office", "Admin")
        };

        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        var student = users[0];
        var kwame = users[2];
        var ama = users[3];

        // Coordinates are the real OpenStreetMap positions of each landmark.
        // The main gate is placed on the south-west approach from the Legon road.
        var balme = new TransitRoute
        {
            Code = "BAL",
            Name = "Balme Library Circular",
            Description = "Short loop around the academic core: the Great Hall, Balme Library, the Business School and Volta Hall.",
            ColorHex = "#2563eb",
            HeadwayMinutes = 6,
            Stops =
            {
                Stop("Great Hall", 5.65068, -0.18756, 1),
                Stop("Balme Library", 5.65113, -0.18703, 2),
                Stop("UG Business School", 5.65281, -0.18863, 3),
                Stop("Volta Hall", 5.65173, -0.18948, 4)
            }
        };

        var halls = new TransitRoute
        {
            Code = "HALL",
            Name = "Hall Residences Connector",
            Description = "Links the southern halls — Legon, Mensah Sarbah and Akuafo — with the Balme Library.",
            ColorHex = "#059669",
            HeadwayMinutes = 10,
            Stops =
            {
                Stop("Legon Hall", 5.64744, -0.18852, 1),
                Stop("Mensah Sarbah Hall", 5.64538, -0.18708, 2),
                Stop("Akuafo Hall", 5.64745, -0.18558, 3),
                Stop("Balme Library", 5.65113, -0.18703, 4)
            }
        };

        var engineering = new TransitRoute
        {
            Code = "ENG",
            Name = "Engineering & Sciences Express",
            Description = "Direct service to the north-east sciences belt, the School of Engineering and the Botanical Gardens.",
            ColorHex = "#d97706",
            HeadwayMinutes = 12,
            Stops =
            {
                Stop("Balme Library", 5.65113, -0.18703, 1),
                Stop("School of Engineering", 5.65558, -0.18281, 2),
                Stop("Legon Botanical Gardens", 5.66145, -0.18622, 3),
                Stop("Volta Hall", 5.65173, -0.18948, 4)
            }
        };

        var western = new TransitRoute
        {
            Code = "WST",
            Name = "Western Campus Link",
            Description = "Evening connector serving Commonwealth Hall, the western residences and the main gate.",
            ColorHex = "#e11d48",
            HeadwayMinutes = 15,
            Stops =
            {
                Stop("Commonwealth Hall", 5.65052, -0.19265, 1),
                Stop("Volta Hall", 5.65173, -0.18948, 2),
                Stop("Legon Hall", 5.64744, -0.18852, 3),
                Stop("Mensah Sarbah Hall", 5.64538, -0.18708, 4),
                Stop("Main Gate", 5.64549, -0.19123, 5)
            }
        };

        db.Routes.AddRange(balme, halls, engineering, western);
        await db.SaveChangesAsync();

        db.Shuttles.AddRange(
            Shuttle("CT-01", balme, 30, 12, ShuttleStatus.InService, kwame, balme.Stops.First().Latitude, balme.Stops.First().Longitude),
            Shuttle("CT-02", balme, 30, 21, ShuttleStatus.InService, ama, balme.Stops.ElementAt(2).Latitude, balme.Stops.ElementAt(2).Longitude),
            Shuttle("CT-03", halls, 45, 18, ShuttleStatus.InService, null, halls.Stops.First().Latitude, halls.Stops.First().Longitude),
            Shuttle("CT-04", halls, 45, 0, ShuttleStatus.OutOfService, null, halls.Stops.ElementAt(3).Latitude, halls.Stops.ElementAt(3).Longitude),
            Shuttle("CT-05", engineering, 14, 6, ShuttleStatus.InService, null, engineering.Stops.First().Latitude, engineering.Stops.First().Longitude),
            Shuttle("CT-06", western, 14, 3, ShuttleStatus.InService, null, western.Stops.First().Latitude, western.Stops.First().Longitude),
            Shuttle("CT-07", western, 14, 0, ShuttleStatus.Idle, null, western.Stops.ElementAt(4).Latitude, western.Stops.ElementAt(4).Longitude));

        await db.SaveChangesAsync();

        var shuttles = await db.Shuttles.ToListAsync();

        db.Reservations.AddRange(
            new Reservation
            {
                Reference = "UG-260318",
                AppUser = student,
                TransitRoute = balme,
                Shuttle = shuttles.First(s => s.Code == "CT-01"),
                Stop = balme.Stops.First(s => s.Name == "Great Hall"),
                Seats = 2,
                CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
                TravelDateUtc = Today()
            },
            new Reservation
            {
                Reference = "UG-260319",
                AppUser = student,
                TransitRoute = halls,
                Shuttle = shuttles.First(s => s.Code == "CT-03"),
                Stop = halls.Stops.First(s => s.Name == "Legon Hall"),
                Seats = 1,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                TravelDateUtc = Today(),
                Status = ReservationStatus.Completed
            });

        db.ServiceReports.Add(new ServiceReport
        {
            Kind = ReportKind.Delay,
            Message = "Heavy foot traffic outside the Balme Library, running approximately four minutes behind schedule.",
            Shuttle = shuttles.First(s => s.Code == "CT-01"),
            Driver = kwame,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-25)
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Midnight today as an explicit UTC instant. <c>DateTime.Now.Date</c> carries
    /// <see cref="DateTimeKind.Unspecified"/>, which Npgsql refuses to write to a
    /// <c>timestamp with time zone</c> column.
    /// </summary>
    public static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private static AppUser NewUser(IPasswordHasher<AppUser> hasher, string email, string name, UserRole role, string affiliation, string passwordPrefix)
    {
        var user = new AppUser
        {
            Email = email,
            FullName = name,
            Role = role,
            Affiliation = affiliation
        };

        user.PasswordHash = hasher.HashPassword(user, passwordPrefix + DefaultPasswordSuffix);
        return user;
    }

    private static Stop Stop(string name, double latitude, double longitude, int sequence) => new()
    {
        Name = name,
        Latitude = latitude,
        Longitude = longitude,
        Sequence = sequence
    };

    private static Shuttle Shuttle(string code, TransitRoute route, int capacity, int occupancy, ShuttleStatus status, AppUser? driver, double latitude, double longitude) => new()
    {
        Code = code,
        TransitRoute = route,
        Capacity = capacity,
        Occupancy = occupancy,
        Status = status,
        Driver = driver,
        Latitude = latitude,
        Longitude = longitude,
        LastUpdatedUtc = DateTime.UtcNow
    };
}
