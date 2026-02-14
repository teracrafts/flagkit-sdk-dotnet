using FlagKit.Core;
using FlagKit.Errors;
using FlagKit.Http;
using System.Security;

namespace FlagKit;

/// <summary>
/// Configuration for bootstrap flag values with optional signature verification.
/// </summary>
public record BootstrapConfig
{
    /// <summary>
    /// The bootstrap flag values.
    /// </summary>
    public Dictionary<string, object?> Flags { get; init; } = new();

    /// <summary>
    /// Optional HMAC-SHA256 signature for verifying the bootstrap data integrity.
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// Optional timestamp (Unix milliseconds) when the bootstrap data was generated.
    /// Used to verify the data is not stale.
    /// </summary>
    public long? Timestamp { get; init; }
}

/// <summary>
/// Configuration for bootstrap signature verification.
/// </summary>
public record BootstrapVerificationConfig
{
    /// <summary>
    /// Whether bootstrap signature verification is enabled.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Maximum age of bootstrap data in milliseconds.
    /// Default: 86400000 (24 hours).
    /// </summary>
    public long MaxAge { get; init; } = 86400000;

    /// <summary>
    /// Action to take when verification fails.
    /// "warn" - Log a warning but use the bootstrap data.
    /// "error" - Throw an exception and reject the bootstrap data.
    /// "ignore" - Silently ignore verification failures.
    /// Default: "warn".
    /// </summary>
    public string OnFailure { get; init; } = "warn";
}

/// <summary>
/// Configuration for error message sanitization to prevent information leakage.
/// </summary>
public record ErrorSanitizationConfig
{
    /// <summary>
    /// Whether error message sanitization is enabled.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Whether to preserve the original unsanitized message in the exception data.
    /// Should only be enabled in development/debugging scenarios.
    /// Default: false.
    /// </summary>
    public bool PreserveOriginal { get; init; } = false;
}

/// <summary>
/// Configuration for evaluation jitter to protect against cache timing attacks.
/// </summary>
public record EvaluationJitterConfig
{
    /// <summary>
    /// Whether evaluation jitter is enabled.
    /// Default: false.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Minimum jitter delay in milliseconds.
    /// Default: 5.
    /// </summary>
    public int MinMs { get; init; } = 5;

    /// <summary>
    /// Maximum jitter delay in milliseconds.
    /// Default: 15.
    /// </summary>
    public int MaxMs { get; init; } = 15;
}

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
    public const int DefaultMaxPersistedEvents = 10000;
    public static readonly TimeSpan DefaultPersistenceFlushInterval = TimeSpan.FromSeconds(1);

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
    /// Secondary API key for automatic failover on 401 errors.
    /// When the primary key fails with unauthorized, the SDK will automatically retry with this key.
    /// </summary>
    public string? SecondaryApiKey { get; init; }

    /// <summary>
    /// When enabled, throws SecurityException instead of warning when PII is detected without PrivateAttributes.
    /// Default: false (only warns).
    /// </summary>
    public bool StrictPIIMode { get; init; } = false;

    /// <summary>
    /// Fields that should be treated as private and not sent to the server.
    /// PII fields listed here will not trigger warnings or exceptions in strict mode.
    /// </summary>
    public List<string>? PrivateAttributes { get; init; }

    /// <summary>
    /// Whether to sign POST request bodies with HMAC-SHA256.
    /// Default: false.
    /// </summary>
    public bool EnableRequestSigning { get; init; } = false;

    /// <summary>
    /// Whether to encrypt cached data using AES-256-CBC with HMAC-SHA256 authentication.
    /// Key is derived from API key using PBKDF2.
    /// Default: false.
    /// </summary>
    public bool EnableCacheEncryption { get; init; } = false;

    /// <summary>
    /// Whether to persist events to disk for crash-resilient event delivery.
    /// When enabled, events are written to disk before being queued for sending.
    /// Default: false.
    /// </summary>
    public bool PersistEvents { get; init; } = false;

    /// <summary>
    /// Directory path for storing persisted events.
    /// If not specified, uses the OS temp directory with a flagkit subdirectory.
    /// </summary>
    public string? EventStoragePath { get; init; }

    /// <summary>
    /// Maximum number of events to persist to disk.
    /// Oldest events are discarded when this limit is reached.
    /// Default: 10000.
    /// </summary>
    public int MaxPersistedEvents { get; init; } = DefaultMaxPersistedEvents;

    /// <summary>
    /// Interval between flushing events from memory buffer to disk.
    /// Default: 1 second.
    /// </summary>
    public TimeSpan PersistenceFlushInterval { get; init; } = DefaultPersistenceFlushInterval;

    /// <summary>
    /// Configuration for evaluation jitter to protect against cache timing attacks.
    /// When enabled, adds a random delay to flag evaluations.
    /// </summary>
    public EvaluationJitterConfig EvaluationJitter { get; init; } = new();

    /// <summary>
    /// Structured bootstrap configuration with optional signature verification.
    /// Use this instead of Bootstrap when you need signature verification.
    /// </summary>
    public BootstrapConfig? BootstrapConfig { get; init; }

    /// <summary>
    /// Configuration for bootstrap signature verification.
    /// Only used when BootstrapConfig is provided with a Signature.
    /// </summary>
    public BootstrapVerificationConfig BootstrapVerification { get; init; } = new();

    /// <summary>
    /// Configuration for error message sanitization to prevent information leakage.
    /// When enabled, sensitive information like API keys, paths, and IP addresses are redacted from error messages.
    /// </summary>
    public ErrorSanitizationConfig ErrorSanitization { get; init; } = new();

    /// <summary>
    /// Whether real-time streaming is enabled.
    /// When enabled, the SDK uses Server-Sent Events (SSE) for instant flag updates (~200ms latency).
    /// Falls back to polling if streaming fails.
    /// Default: true.
    /// </summary>
    public bool StreamingEnabled { get; init; } = true;

    /// <summary>
    /// Configuration for streaming behavior.
    /// </summary>
    public StreamingConfig Streaming { get; init; } = new();

    /// <summary>
    /// Callback for usage metrics updates from API responses.
    /// Called when usage headers are present in the response.
    /// </summary>
    public Action<UsageMetrics>? OnUsageUpdate { get; init; }

    /// <summary>
    /// Callback when subscription error occurs during streaming (e.g., suspended).
    /// </summary>
    public Action<string>? OnSubscriptionError { get; init; }

    /// <summary>
    /// Callback when connection limit is reached during streaming.
    /// </summary>
    public Action? OnConnectionLimitError { get; init; }

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

        // Validate secondary API key format if provided
        if (!string.IsNullOrWhiteSpace(SecondaryApiKey) && !validPrefixes.Any(p => SecondaryApiKey.StartsWith(p)))
            throw FlagKitException.ConfigError(ErrorCode.ConfigInvalidApiKey, "Invalid secondary API key format");

        // Validate event persistence options
        if (MaxPersistedEvents <= 0)
            throw FlagKitException.ConfigError(ErrorCode.ConfigInvalidPollingInterval, "MaxPersistedEvents must be positive");

        if (PersistenceFlushInterval <= TimeSpan.Zero)
            throw FlagKitException.ConfigError(ErrorCode.ConfigInvalidPollingInterval, "PersistenceFlushInterval must be positive");
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
        private string? _secondaryApiKey;
        private bool _strictPIIMode = false;
        private List<string>? _privateAttributes;
        private bool _enableRequestSigning = false;
        private bool _enableCacheEncryption = false;
        private bool _persistEvents = false;
        private string? _eventStoragePath;
        private int _maxPersistedEvents = DefaultMaxPersistedEvents;
        private TimeSpan _persistenceFlushInterval = DefaultPersistenceFlushInterval;
        private EvaluationJitterConfig _evaluationJitter = new();
        private BootstrapConfig? _bootstrapConfig;
        private BootstrapVerificationConfig _bootstrapVerification = new();
        private ErrorSanitizationConfig _errorSanitization = new();
        private bool _streamingEnabled = true;
        private StreamingConfig _streaming = new();
        private Action<UsageMetrics>? _onUsageUpdate;
        private Action<string>? _onSubscriptionError;
        private Action? _onConnectionLimitError;

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
        public Builder SecondaryApiKey(string key) { _secondaryApiKey = key; return this; }
        public Builder StrictPIIMode(bool enabled) { _strictPIIMode = enabled; return this; }
        public Builder PrivateAttributes(List<string> attributes) { _privateAttributes = attributes; return this; }
        public Builder EnableRequestSigning(bool enabled) { _enableRequestSigning = enabled; return this; }
        public Builder EnableCacheEncryption(bool enabled) { _enableCacheEncryption = enabled; return this; }
        public Builder PersistEvents(bool enabled) { _persistEvents = enabled; return this; }
        public Builder EventStoragePath(string path) { _eventStoragePath = path; return this; }
        public Builder MaxPersistedEvents(int max) { _maxPersistedEvents = max; return this; }
        public Builder PersistenceFlushInterval(TimeSpan interval) { _persistenceFlushInterval = interval; return this; }
        public Builder EvaluationJitter(EvaluationJitterConfig config) { _evaluationJitter = config; return this; }
        public Builder BootstrapConfig(BootstrapConfig config) { _bootstrapConfig = config; return this; }
        public Builder BootstrapVerification(BootstrapVerificationConfig config) { _bootstrapVerification = config; return this; }
        public Builder ErrorSanitization(ErrorSanitizationConfig config) { _errorSanitization = config; return this; }
        public Builder StreamingEnabled(bool enabled) { _streamingEnabled = enabled; return this; }
        public Builder Streaming(StreamingConfig config) { _streaming = config; return this; }
        public Builder OnUsageUpdate(Action<UsageMetrics> callback) { _onUsageUpdate = callback; return this; }
        public Builder OnSubscriptionError(Action<string> callback) { _onSubscriptionError = callback; return this; }
        public Builder OnConnectionLimitError(Action callback) { _onConnectionLimitError = callback; return this; }

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
            SecondaryApiKey = _secondaryApiKey,
            StrictPIIMode = _strictPIIMode,
            PrivateAttributes = _privateAttributes,
            EnableRequestSigning = _enableRequestSigning,
            EnableCacheEncryption = _enableCacheEncryption,
            PersistEvents = _persistEvents,
            EventStoragePath = _eventStoragePath,
            MaxPersistedEvents = _maxPersistedEvents,
            PersistenceFlushInterval = _persistenceFlushInterval,
            EvaluationJitter = _evaluationJitter,
            BootstrapConfig = _bootstrapConfig,
            BootstrapVerification = _bootstrapVerification,
            ErrorSanitization = _errorSanitization,
            StreamingEnabled = _streamingEnabled,
            Streaming = _streaming,
            OnUsageUpdate = _onUsageUpdate,
            OnSubscriptionError = _onSubscriptionError,
            OnConnectionLimitError = _onConnectionLimitError
        };
    }

    public static Builder CreateBuilder(string apiKey) => new(apiKey);
}
