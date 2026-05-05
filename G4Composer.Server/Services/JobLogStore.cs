using System.Collections.Concurrent;
using System.Text;

namespace G4Composer.Server.Services;

/// <summary>
/// Stores Docker step logs per job in memory. Entries are evicted automatically
/// after <see cref="Ttl"/> to prevent unbounded memory growth.
/// </summary>
public interface IJobLogStore
{
    void Append(string jobId, string line);
    string Get(string jobId);
}

public sealed class InMemoryJobLogStore : IJobLogStore, IDisposable
{
    private static readonly TimeSpan Ttl           = TimeSpan.FromHours(1);
    private static readonly TimeSpan CleanupPeriod = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Entry> _store = new();
    private readonly Timer _cleanupTimer;

    public InMemoryJobLogStore()
    {
        _cleanupTimer = new Timer(_ => Cleanup(), null, CleanupPeriod, CleanupPeriod);
    }

    public void Append(string jobId, string line)
    {
        var entry = _store.GetOrAdd(jobId, _ => new Entry());
        entry.Append(line);
    }

    public string Get(string jobId)
        => _store.TryGetValue(jobId, out var entry) ? entry.ToString() : string.Empty;

    private void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow - Ttl;
        foreach (var (key, entry) in _store)
            if (entry.Created < cutoff)
                _store.TryRemove(key, out _);
    }

    public void Dispose() => _cleanupTimer.Dispose();

    private sealed class Entry
    {
        private readonly StringBuilder _sb = new();
        private readonly Lock _lock = new();
        public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;

        public void Append(string line)
        {
            lock (_lock) { _sb.AppendLine(line); }
        }

        public override string ToString()
        {
            lock (_lock) { return _sb.ToString(); }
        }
    }
}
