using System.Collections.Concurrent;
using FlagKit.Types;

namespace FlagKit.Core;

/// <summary>
/// Thread-safe in-memory cache with TTL, LRU eviction, and stale value fallback.
/// </summary>
/// <typeparam name="TKey">The type of the cache keys.</typeparam>
/// <typeparam name="TValue">The type of the cached values.</typeparam>
public class Cache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, CacheEntry> _cache = new();
    private readonly int _maxSize;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _staleTtl;
    private readonly object _evictionLock = new();

    /// <summary>
    /// Creates a new cache instance.
    /// </summary>
    /// <param name="maxSize">Maximum number of entries (default: 1000).</param>
    /// <param name="ttl">Time-to-live for cache entries (default: 5 minutes).</param>
    /// <param name="staleTtl">Additional time entries can be served as stale (default: 1 hour).</param>
    public Cache(int maxSize = 1000, TimeSpan? ttl = null, TimeSpan? staleTtl = null)
    {
        _maxSize = maxSize > 0 ? maxSize : 1000;
        _ttl = ttl ?? TimeSpan.FromMinutes(5);
        _staleTtl = staleTtl ?? TimeSpan.FromHours(1);
    }

    /// <summary>
    /// Gets a value from the cache.
    /// Returns null/default if not found or expired.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <returns>The cached value or default.</returns>
    public TValue? Get(TKey key)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTime.UtcNow < entry.ExpiresAt)
            {
                entry.LastAccessed = DateTime.UtcNow;
                return entry.Value;
            }

            // Entry is expired but might be within stale TTL
            // Remove it for fresh access
            _cache.TryRemove(key, out _);
        }
        return default;
    }

    /// <summary>
    /// Gets a value from the cache, allowing stale values as fallback.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="allowStale">Whether to return stale values.</param>
    /// <param name="isStale">Output parameter indicating if the returned value is stale.</param>
    /// <returns>The cached value or default.</returns>
    public TValue? Get(TKey key, bool allowStale, out bool isStale)
    {
        isStale = false;

        if (_cache.TryGetValue(key, out var entry))
        {
            var now = DateTime.UtcNow;
            entry.LastAccessed = now;

            if (now < entry.ExpiresAt)
            {
                // Fresh value
                return entry.Value;
            }

            if (allowStale && now < entry.StaleAt)
            {
                // Stale but within grace period
                isStale = true;
                return entry.Value;
            }

            // Completely expired
            _cache.TryRemove(key, out _);
        }

        return default;
    }

    /// <summary>
    /// Gets a value or adds it using the provided factory if not found.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="valueFactory">Factory to create the value if not cached.</param>
    /// <param name="customTtl">Optional custom TTL for this entry.</param>
    /// <returns>The cached or newly created value.</returns>
    public TValue GetOrAdd(TKey key, Func<TValue> valueFactory, TimeSpan? customTtl = null)
    {
        if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow < entry.ExpiresAt)
        {
            entry.LastAccessed = DateTime.UtcNow;
            return entry.Value;
        }

        var value = valueFactory();
        Set(key, value, customTtl);
        return value;
    }

    /// <summary>
    /// Sets a value in the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="customTtl">Optional custom TTL for this entry.</param>
    public void Set(TKey key, TValue value, TimeSpan? customTtl = null)
    {
        var ttl = customTtl ?? _ttl;
        var now = DateTime.UtcNow;
        var entry = new CacheEntry
        {
            Value = value,
            ExpiresAt = now + ttl,
            StaleAt = now + ttl + _staleTtl,
            LastAccessed = now
        };

        _cache[key] = entry;
        EvictIfNeeded();
    }

    /// <summary>
    /// Checks if the cache contains a valid (non-expired) entry for the key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <returns>True if the key exists and is not expired.</returns>
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

    /// <summary>
    /// Checks if the cache contains an entry (including stale) for the key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="includeStale">Whether to include stale entries.</param>
    /// <returns>True if the key exists.</returns>
    public bool Has(TKey key, bool includeStale)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            var now = DateTime.UtcNow;
            if (now < entry.ExpiresAt)
                return true;
            if (includeStale && now < entry.StaleAt)
                return true;
            _cache.TryRemove(key, out _);
        }
        return false;
    }

    /// <summary>
    /// Removes an entry from the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <returns>True if the entry was removed.</returns>
    public bool Remove(TKey key) => _cache.TryRemove(key, out _);

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    public void Clear() => _cache.Clear();

    /// <summary>
    /// Gets the number of entries in the cache (including expired).
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Gets all keys in the cache.
    /// </summary>
    public IEnumerable<TKey> Keys => _cache.Keys;

    /// <summary>
    /// Removes all expired entries from the cache.
    /// </summary>
    public void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _cache
            .Where(kvp => now >= kvp.Value.StaleAt)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private void EvictIfNeeded()
    {
        if (_cache.Count <= _maxSize) return;

        lock (_evictionLock)
        {
            if (_cache.Count <= _maxSize) return;

            // Remove expired entries first
            var now = DateTime.UtcNow;
            var expiredKeys = _cache
                .Where(kvp => now >= kvp.Value.StaleAt)
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
        public required DateTime StaleAt { get; init; }
        public DateTime LastAccessed { get; set; }
    }
}

/// <summary>
/// Specialized cache for flag states with additional convenience methods.
/// </summary>
public class FlagCache : Cache<string, FlagState>
{
    /// <summary>
    /// Creates a new flag cache.
    /// </summary>
    /// <param name="maxSize">Maximum number of flags (default: 1000).</param>
    /// <param name="ttl">Time-to-live for cache entries (default: 5 minutes).</param>
    /// <param name="staleTtl">Additional time entries can be served as stale (default: 1 hour).</param>
    public FlagCache(int maxSize = 1000, TimeSpan? ttl = null, TimeSpan? staleTtl = null)
        : base(maxSize, ttl, staleTtl)
    {
    }

    /// <summary>
    /// Sets multiple flags in the cache.
    /// </summary>
    /// <param name="flags">The flags to cache.</param>
    public void SetAll(IEnumerable<FlagState> flags)
    {
        foreach (var flag in flags)
            Set(flag.Key, flag);
    }

    /// <summary>
    /// Gets all valid (non-expired) flags from the cache.
    /// </summary>
    /// <returns>Dictionary of flag keys to flag states.</returns>
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

    /// <summary>
    /// Gets all flags including stale ones.
    /// </summary>
    /// <param name="includeStale">Whether to include stale entries.</param>
    /// <returns>Dictionary of flag keys to flag states with stale indication.</returns>
    public IReadOnlyDictionary<string, (FlagState Flag, bool IsStale)> GetAllWithStaleInfo(bool includeStale = false)
    {
        var result = new Dictionary<string, (FlagState, bool)>();
        foreach (var key in Keys)
        {
            var value = Get(key, includeStale, out var isStale);
            if (value != null)
                result[key] = (value, isStale);
        }
        return result;
    }

    /// <summary>
    /// Gets a flag value, with stale fallback support.
    /// </summary>
    /// <param name="key">The flag key.</param>
    /// <param name="allowStale">Whether to allow stale values.</param>
    /// <param name="isStale">Output indicating if the value is stale.</param>
    /// <returns>The flag state or null.</returns>
    public FlagState? GetWithStaleSupport(string key, bool allowStale, out bool isStale)
    {
        return Get(key, allowStale, out isStale);
    }
}
