using System.Collections.Concurrent;

namespace Algorithm.CoastGuard.SmugglingDetection;

internal sealed class TtlFlagManager<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, DateTime> _expiresAtUtc = new();

    public void SetValue(TKey key, bool isActive, int ttlSeconds)
    {
        if (!isActive)
        {
            _expiresAtUtc.TryRemove(key, out _);
            return;
        }

        _expiresAtUtc[key] = DateTime.UtcNow.AddSeconds(Math.Max(1, ttlSeconds));
    }

    public bool TryGetValue(TKey key, out bool isActive)
    {
        isActive = false;

        if (!_expiresAtUtc.TryGetValue(key, out var expiresAtUtc))
        {
            return false;
        }

        if (expiresAtUtc <= DateTime.UtcNow)
        {
            _expiresAtUtc.TryRemove(key, out _);
            return false;
        }

        isActive = true;
        return true;
    }

    public void Clear()
    {
        _expiresAtUtc.Clear();
    }
}
