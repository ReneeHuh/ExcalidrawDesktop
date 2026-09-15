namespace ExcalidrawDesktop.Core;

/// <summary>Tracks recovery files that must survive cleanup until explicitly released.</summary>
public sealed class RecoveryRetentionPolicy
{
    private readonly HashSet<string> preserved = new(StringComparer.OrdinalIgnoreCase);
    private bool discoveryFailed;
    public void Reset() { preserved.Clear(); discoveryFailed = false; }
    public void Seed(IEnumerable<string> ids) { foreach (var id in ids) if (Guid.TryParseExact(id, "N", out var g)) preserved.Add(g.ToString("N")); }
    public void MarkDiscoveryFailed() => discoveryFailed = true;
    public void Release(string id) { if (Guid.TryParseExact(id, "N", out var g)) preserved.Remove(g.ToString("N")); }
    public IReadOnlySet<string> GetRetained(IEnumerable<string> live) =>
        preserved.Concat(live).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public bool ShouldSkipPrune => discoveryFailed;
}
