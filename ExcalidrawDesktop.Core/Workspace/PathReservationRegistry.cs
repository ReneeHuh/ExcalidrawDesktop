namespace ExcalidrawDesktop.Core;

public sealed class PathReservationRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<string, object> reservations = new(StringComparer.OrdinalIgnoreCase);
    public bool TryReserve(string path, object owner, out IDisposable? lease)
    {
        lease = null;
        var canonical = DesktopDocumentPath.Normalize(path);
        if (canonical is null) return false;
        lock (gate)
        {
            if (reservations.ContainsKey(canonical)) return false;
            reservations[canonical] = owner;
            lease = new Lease(this, canonical, owner);
            return true;
        }
    }
    public bool IsReservedByAnother(string path, object owner)
    {
        var canonical = DesktopDocumentPath.Normalize(path);
        lock (gate) return canonical is not null && reservations.TryGetValue(canonical, out var found) && !ReferenceEquals(found, owner);
    }
    private void Release(string path, object owner) { lock (gate) if (reservations.TryGetValue(path, out var found) && ReferenceEquals(found, owner)) reservations.Remove(path); }
    private sealed class Lease(PathReservationRegistry registry, string path, object owner) : IDisposable
    { private int disposed; public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) registry.Release(path, owner); } }
}
