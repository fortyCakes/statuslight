namespace StatusLight.Core;

/// <summary>Thread-safe set of current signals, keyed by (source, key), with TTL expiry.</summary>
public sealed class SignalStore(TimeProvider time)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Source, string Key), StatusSignal> _signals = [];

    /// <summary>Adds or replaces a signal. Updates older than the stored one are ignored (e.g. relay messages replayed on startup).</summary>
    public void Upsert(StatusSignal signal)
    {
        lock (_lock)
        {
            var id = (signal.Source, signal.Key);
            if (_signals.TryGetValue(id, out var existing) && existing.UpdatedAt > signal.UpdatedAt)
                return;

            _signals[id] = signal;
        }
    }

    public bool Remove(string source, string key)
    {
        lock (_lock)
            return _signals.Remove((source, key));
    }

    /// <summary>Returns all unexpired signals, dropping expired ones from the store.</summary>
    public IReadOnlyList<StatusSignal> GetActive()
    {
        var now = time.GetUtcNow();
        lock (_lock)
        {
            var expired = _signals.Where(kv => kv.Value.ExpiresAt <= now).Select(kv => kv.Key).ToList();
            foreach (var id in expired)
                _signals.Remove(id);

            return [.. _signals.Values];
        }
    }
}
