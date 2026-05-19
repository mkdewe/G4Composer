using System.Collections.Concurrent;

namespace G4Composer.Server.Services;

/// <summary>
/// Stores per-step PDB bytes for completed multi-step jobs.
/// Keyed by (jobId, engine, step). Entries expire after TTL.
/// </summary>
public interface IFrameStore
{
    void Store(string jobId, string engine, int step, byte[] pdb);
    byte[]? Get(string jobId, string engine, int step);
    IReadOnlyList<int> Steps(string jobId, string engine);
}

public sealed class InMemoryFrameStore : IFrameStore, IDisposable
{
    private static readonly TimeSpan Ttl           = TimeSpan.FromHours(1);
    private static readonly TimeSpan CleanupPeriod = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Entry> _store = new();
    private readonly Timer _cleanupTimer;

    public InMemoryFrameStore()
        => _cleanupTimer = new Timer(_ => Cleanup(), null, CleanupPeriod, CleanupPeriod);

    public void Store(string jobId, string engine, int step, byte[] pdb)
        => _store[Key(jobId, engine, step)] = new Entry(pdb);

    public byte[]? Get(string jobId, string engine, int step)
        => _store.TryGetValue(Key(jobId, engine, step), out var e) ? e.Pdb : null;

    public IReadOnlyList<int> Steps(string jobId, string engine)
    {
        var prefix = $"{jobId}|{engine}|";
        return _store.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => int.TryParse(k[prefix.Length..], out var s) ? s : -1)
            .Where(s => s >= 0)
            .OrderBy(s => s)
            .ToList();
    }

    private static string Key(string jobId, string engine, int step)
        => $"{jobId}|{engine}|{step}";

    private void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow - Ttl;
        foreach (var (key, entry) in _store)
            if (entry.Created < cutoff)
                _store.TryRemove(key, out _);
    }

    public void Dispose() => _cleanupTimer.Dispose();

    private sealed record Entry(byte[] Pdb)
    {
        public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;
    }
}
