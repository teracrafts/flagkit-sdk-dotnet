using FlagKit.Errors;

namespace FlagKit;

/// <summary>
/// Configuration options for the FlagKit SDK.
/// </summary>
public record FlagKitOptions
{
    public static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromSeconds(300);
    public const int DefaultMaxCacheSize = 1000;
    public const int DefaultEventBatchSize = 10;
    public static readonly TimeSpan DefaultEventFlushInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    public const int DefaultRetryAttempts = 3;
    public const int DefaultCircuitBreakerThreshold = 5;
    public static readonly TimeSpan DefaultCircuitBreakerResetTimeout = TimeSpan.FromSeconds(30);

    public required string ApiKey { get; init; }
    public TimeSpan PollingInterval { get; init; } = DefaultPollingInterval;
    public TimeSpan CacheTtl { get; init; } = DefaultCacheTtl;
    public int MaxCacheSize { get; init; } = DefaultMaxCacheSize;
    public bool CacheEnabled { get; init; } = true;
    public int EventBatchSize { get; init; } = DefaultEventBatchSize;
    public TimeSpan EventFlushInterval { get; init; } = DefaultEventFlushInterval;
    public bool EventsEnabled { get; init; } = true;
    public TimeSpan Timeout { get; init; } = DefaultTimeout;
    public int RetryAttempts { get; init; } = DefaultRetryAttempts;
    public int CircuitBreakerThreshold { get; init; } = DefaultCircuitBreakerThreshold;
    public TimeSpan CircuitBreakerResetTimeout { get; init; } = DefaultCircuitBreakerResetTimeout;
    public Dictionary<string, object>? Bootstrap { get; init; }
    /// <summary>
    /// Local development server port. When set, uses http://localhost:{port}/api/v1.
    /// </summary>
    public int? LocalPort { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw FlagKitException.ConfigError(ErrorCode.ConfigInvalidApiKey, "API key is required");

        var validPrefixes = new[] { "sdk_", "srv_", "cli_" };
        if (!validPrefixes.Any(p => ApiKey.StartsWith(p)))
            throw FlagKitException.ConfigError(ErrorCode.ConfigInvalidApiKey, "Invalid API key format");

        if (PollingInterval <= TimeSpan.Zero)
            throw FlagKitException.ConfigError(ErrorCode.ConfigInvalidPollingInterval, "Polling interval must be positive");

        if (CacheTtl <= TimeSpan.Zero)
            throw FlagKitException.ConfigError(ErrorCode.ConfigInvalidCacheTtl, "Cache TTL must be positive");
    }

    public class Builder
    {
        private readonly string _apiKey;
        private TimeSpan _pollingInterval = DefaultPollingInterval;
        private TimeSpan _cacheTtl = DefaultCacheTtl;
        private int _maxCacheSize = DefaultMaxCacheSize;
        private bool _cacheEnabled = true;
        private int _eventBatchSize = DefaultEventBatchSize;
        private TimeSpan _eventFlushInterval = DefaultEventFlushInterval;
        private bool _eventsEnabled = true;
        private TimeSpan _timeout = DefaultTimeout;
        private int _retryAttempts = DefaultRetryAttempts;
        private Dictionary<string, object>? _bootstrap;
        private int? _localPort;

        public Builder(string apiKey) => _apiKey = apiKey;

        public Builder PollingInterval(TimeSpan interval) { _pollingInterval = interval; return this; }
        public Builder CacheTtl(TimeSpan ttl) { _cacheTtl = ttl; return this; }
        public Builder MaxCacheSize(int size) { _maxCacheSize = size; return this; }
        public Builder CacheEnabled(bool enabled) { _cacheEnabled = enabled; return this; }
        public Builder EventBatchSize(int size) { _eventBatchSize = size; return this; }
        public Builder EventFlushInterval(TimeSpan interval) { _eventFlushInterval = interval; return this; }
        public Builder EventsEnabled(bool enabled) { _eventsEnabled = enabled; return this; }
        public Builder Timeout(TimeSpan timeout) { _timeout = timeout; return this; }
        public Builder RetryAttempts(int attempts) { _retryAttempts = attempts; return this; }
        public Builder Bootstrap(Dictionary<string, object> data) { _bootstrap = data; return this; }
        public Builder LocalPort(int port) { _localPort = port; return this; }

        public FlagKitOptions Build() => new()
        {
            ApiKey = _apiKey,
            PollingInterval = _pollingInterval,
            CacheTtl = _cacheTtl,
            MaxCacheSize = _maxCacheSize,
            CacheEnabled = _cacheEnabled,
            EventBatchSize = _eventBatchSize,
            EventFlushInterval = _eventFlushInterval,
            EventsEnabled = _eventsEnabled,
            Timeout = _timeout,
            RetryAttempts = _retryAttempts,
            Bootstrap = _bootstrap,
            LocalPort = _localPort
        };
    }

    public static Builder CreateBuilder(string apiKey) => new(apiKey);
}
