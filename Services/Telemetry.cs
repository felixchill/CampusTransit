using CampusTransit.Data;

namespace CampusTransit.Services;

/// <summary>
/// Projects a shuttle's loop progress onto its route polyline so the UI can show a
/// believable position, the next stop and an estimated arrival time.
/// </summary>
public static class Telemetry
{
    /// <summary>Seconds a shuttle needs for one full loop of its route during the demo simulation.</summary>
    public static double LoopSeconds(int stopCount) => 60 + (stopCount * 20);

    public static IReadOnlyList<Stop> Ordered(Shuttle shuttle) =>
        shuttle.TransitRoute?.Stops.OrderBy(s => s.Sequence).ToList() ?? [];

    public static (double Latitude, double Longitude) Position(Shuttle shuttle)
    {
        var stops = Ordered(shuttle);
        if (stops.Count == 0)
        {
            return (shuttle.Latitude, shuttle.Longitude);
        }

        if (stops.Count == 1)
        {
            return (stops[0].Latitude, stops[0].Longitude);
        }

        var progress = Math.Clamp(shuttle.Progress, 0, 0.999999);
        var scaled = progress * stops.Count;
        var segment = (int)Math.Floor(scaled);
        var t = scaled - segment;

        var from = stops[segment];
        var to = stops[(segment + 1) % stops.Count];

        return (Lerp(from.Latitude, to.Latitude, t), Lerp(from.Longitude, to.Longitude, t));
    }

    public static Stop? NextStop(Shuttle shuttle)
    {
        var stops = Ordered(shuttle);
        if (stops.Count == 0)
        {
            return null;
        }

        var progress = Math.Clamp(shuttle.Progress, 0, 0.999999);
        var segment = (int)Math.Floor(progress * stops.Count);
        return stops[(segment + 1) % stops.Count];
    }

    public static int EtaMinutes(Shuttle shuttle)
    {
        var stops = Ordered(shuttle);
        if (stops.Count == 0 || shuttle.TransitRoute is null)
        {
            return 0;
        }

        var progress = Math.Clamp(shuttle.Progress, 0, 0.999999);
        var scaled = progress * stops.Count;
        var remaining = 1 - (scaled - Math.Floor(scaled));
        var segmentMinutes = shuttle.TransitRoute.HeadwayMinutes / (double)stops.Count;

        return Math.Max(1, (int)Math.Ceiling(remaining * segmentMinutes));
    }

    public static ShuttleUpdate ToUpdate(Shuttle shuttle)
    {
        var (latitude, longitude) = Position(shuttle);

        return new ShuttleUpdate(
            shuttle.Id,
            shuttle.Code,
            shuttle.TransitRoute?.Code,
            shuttle.TransitRoute?.Name,
            shuttle.TransitRoute?.ColorHex ?? "#64748b",
            Math.Round(latitude, 6),
            Math.Round(longitude, 6),
            shuttle.Occupancy,
            shuttle.Capacity,
            shuttle.SeatsAvailable,
            shuttle.Status.ToString(),
            NextStop(shuttle)?.Name,
            EtaMinutes(shuttle),
            DateTime.UtcNow);
    }

    /// <summary>Every stop of a route plus the closing point, ready to draw as a polyline.</summary>
    public static IReadOnlyList<(double Latitude, double Longitude)> Polyline(TransitRoute route)
    {
        var points = route.Stops
            .OrderBy(s => s.Sequence)
            .Select(s => (s.Latitude, s.Longitude))
            .ToList();

        if (points.Count > 1)
        {
            points.Add(points[0]);
        }

        return points;
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);
}
