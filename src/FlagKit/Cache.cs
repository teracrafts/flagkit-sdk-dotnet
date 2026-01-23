using System.Collections.Concurrent;

namespace FlagKit;

/// <summary>
/// Thread-safe in-memory cache with TTL and LRU eviction.
/// </summary>
public class Cache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, CacheEntry> _cache = new();
    private readonly int _maxSize;
    private readonly TimeSpan _ttl;
    private readonly object _evictionLock = new();

    public Cache(int maxSize = 1000, TimeSpan? ttl = null)
    {
        _maxSize = maxSize;
        _ttl = ttl ?? TimeSpan.FromMinutes(5);
    }

    public TValue? Get(TKey key)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTime.UtcNow < entry.ExpiresAt)
            {
                entry.LastAccessed = DateTime.UtcNow;
                return entry.Value;
            }
            _cache.TryRemove(key, out _);
        }
        return default;
    }

    public void Set(TKey key, TValue value, TimeSpan? customTtl = null)
    {
        var ttl = customTtl ?? _ttl;
        var entry = new CacheEntry
        {
            Value = value,
            ExpiresAt = DateTime.UtcNow + ttl,
            LastAccessed = DateTime.UtcNow
        };

        _cache[key] = entry;
        EvictIfNeeded();
    }

    public bool Has(TKey key)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTime.UtcNow < entry.ExpiresAt)
                return true;
            _cache.TryRemove(key, out _);
        }
        return false;
    }

    public bool Remove(TKey key) => _cache.TryRemove(key, out _);

    public void Clear() => _cache.Clear();

    public int Count => _cache.Count;

    public IEnumerable<TKey> Keys => _cache.Keys;

    private void EvictIfNeeded()
    {
        if (_cache.Count <= _maxSize) return;

        lock (_evictionLock)
        {
            if (_cache.Count <= _maxSize) return;

            // Remove expired entries first
            var now = DateTime.UtcNow;
            var expiredKeys = _cache
                .Where(kvp => now >= kvp.Value.ExpiresAt)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
                _cache.TryRemove(key, out _);

            // If still over capacity, remove least recently used
            while (_cache.Count > _maxSize)
            {
                var lruKey = _cache
                    .OrderBy(kvp => kvp.Value.LastAccessed)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault();

                if (lruKey != null)
                    _cache.TryRemove(lruKey, out _);
                else
                    break;
            }
        }
    }

    private class CacheEntry
    {
        public required TValue Value { get; init; }
        public required DateTime ExpiresAt { get; init; }
        public DateTime LastAccessed { get; set; }
    }
}

/// <summary>
/// Specialized cache for flag states.
/// </summary>
public class FlagCache : Cache<string, FlagState>
{
    public FlagCache(int maxSize = 1000, TimeSpan? ttl = null)
        : base(maxSize, ttl)
    {
    }

    public void SetAll(IEnumerable<FlagState> flags)
    {
        foreach (var flag in flags)
            Set(flag.Key, flag);
    }

    public IReadOnlyDictionary<string, FlagState> GetAll()
    {
        var result = new Dictionary<string, FlagState>();
        foreach (var key in Keys)
        {
            var value = Get(key);
            if (value != null)
                result[key] = value;
        }
        return result;
    }
}
