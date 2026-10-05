namespace CampusTransit.Services;

/// <summary>
/// Counts the clients currently connected to the transit hub. The shuttle simulation only
/// advances while at least one client is listening, which keeps an always-on deployment
/// inside a free hosting plan's CPU allowance instead of ticking every two seconds forever.
/// </summary>
public class TransitPresence
{
    private int _clients;

    public int Clients => Volatile.Read(ref _clients);

    public bool HasClients => Clients > 0;

    public int Join() => Interlocked.Increment(ref _clients);

    public int Leave() => Math.Max(0, Interlocked.Decrement(ref _clients));
}
