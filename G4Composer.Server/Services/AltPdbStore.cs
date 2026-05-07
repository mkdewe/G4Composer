using System.Collections.Concurrent;

namespace G4Composer.Server.Services;

/// <summary>
/// Stores the alternative-engine PDB bytes in memory per job ID.
/// Entries are evicted automatically after TTL.
/// </summary>
public interface IAltPdbStore
{
    void Store(string jobId, byte[] pdb);
    byte[]? Get(string jobId);
}

public sealed class InMemoryAltPdbStore : IAltPdbStore, IDisposable
{
    private static readonly TimeSpan Ttl           = TimeSpan.FromHours(1);
    private static readonly TimeSpan CleanupPeriod = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Entry> _store = new();
    private readonly Timer _cleanupTimer;

    public InMemoryAltPdbStore()
    {
        _cleanupTimer = new Timer(_ => Cleanup(), null, CleanupPeriod, CleanupPeriod);
    }

    public void Store(string jobId, byte[] pdb)
        => _store[jobId] = new Entry(pdb);

    public byte[]? Get(string jobId)
        => _store.TryGetValue(jobId, out var entry) ? entry.Pdb : null;

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
