using System.Text.Json.Serialization;
using FlagKit.Core;
using FlagKit.Errors;
using FlagKit.Http;
using FlagKit.Types;

namespace FlagKit;

/// <summary>
/// Response from SDK init endpoint.
/// </summary>
public record InitResponse
{
    [JsonPropertyName("flags")]
    public required List<FlagState> Flags { get; init; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    [JsonPropertyName("pollingIntervalSeconds")]
    public int? PollingIntervalSeconds { get; init; }

    [JsonPropertyName("environment")]
    public string? Environment { get; init; }

    [JsonPropertyName("environmentId")]
    public string? EnvironmentId { get; init; }

    [JsonPropertyName("projectId")]
    public string? ProjectId { get; init; }
}

/// <summary>
/// Response from SDK updates endpoint.
/// </summary>
public record UpdatesResponse
{
    [JsonPropertyName("flags")]
    public List<FlagState>? Flags { get; init; }

    [JsonPropertyName("hasUpdates")]
    public bool HasUpdates { get; init; }

    [JsonPropertyName("checkedAt")]
    public string? CheckedAt { get; init; }

    [JsonPropertyName("since")]
    public string? Since { get; init; }
}

/// <summary>
/// Request for single flag evaluation.
/// </summary>
public record EvaluateRequest
{
    [JsonPropertyName("flagKey")]
    public required string FlagKey { get; init; }

    [JsonPropertyName("context")]
    public Dictionary<string, object?>? Context { get; init; }
}

/// <summary>
/// Request for batch flag evaluation.
/// </summary>
public record BatchEvaluateRequest
{
    [JsonPropertyName("flagKeys")]
    public List<string>? FlagKeys { get; init; }

    [JsonPropertyName("context")]
    public Dictionary<string, object?>? Context { get; init; }
}

/// <summary>
/// Request for evaluating all flags.
/// </summary>
public record EvaluateAllRequest
{
    [JsonPropertyName("context")]
    public Dictionary<string, object?>? Context { get; init; }
}

/// <summary>
/// Response from evaluate endpoint.
/// </summary>
public record EvaluateResponse
{
    [JsonPropertyName("flagKey")]
    public required string FlagKey { get; init; }

    [JsonPropertyName("value")]
    public required FlagValue Value { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("reason")]
    public EvaluationReason Reason { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("variationId")]
    public string? VariationId { get; init; }

    [JsonPropertyName("ruleId")]
    public string? RuleId { get; init; }

    [JsonPropertyName("segmentId")]
    public string? SegmentId { get; init; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }
}

/// <summary>
/// Response from batch evaluate endpoint.
/// </summary>
public record BatchEvaluateResponse
{
    [JsonPropertyName("flags")]
    public required Dictionary<string, EvaluateResponse> Flags { get; init; }

    [JsonPropertyName("evaluatedAt")]
    public string? EvaluatedAt { get; init; }
}

/// <summary>
/// Main FlagKit client for feature flag evaluation.
/// Implements IDisposable and IAsyncDisposable for proper resource cleanup.
/// </summary>
public class FlagKitClient : IDisposable, IAsyncDisposable
{
    private readonly FlagKitOptions _options;
    private readonly FlagKitHttpClient _httpClient;
    private readonly FlagCache _cache;
    private readonly PollingManager _pollingManager;
    private readonly EventQueue? _eventQueue;
    private readonly ContextManager _contextManager;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    private bool _initialized;
    private bool _disposed;
    private TaskCompletionSource<bool>? _readyTcs;
    private DateTime? _lastServerTime;

    /// <summary>
    /// Creates a new FlagKit client with the specified options.
    /// </summary>
    /// <param name="options">Configuration options for the client.</param>
    public FlagKitClient(FlagKitOptions options)
    {
        options.Validate();
        _options = options;

        _httpClient = new FlagKitHttpClient(options);
        _cache = new FlagCache(options.MaxCacheSize, options.CacheTtl);
        _contextManager = new ContextManager();

        _pollingManager = new PollingManager(
            options.PollingInterval,
            async since => await PollForUpdatesAsync(since));

        if (options.EventsEnabled)
        {
            _eventQueue = new EventQueue(
                options.EventBatchSize,
                options.EventFlushInterval,
                async events => await SendEventsAsync(events));
        }

        // Load bootstrap data if provided
        if (options.Bootstrap != null)
        {
            LoadBootstrap(options.Bootstrap);
        }
    }

    /// <summary>
    /// Gets whether the SDK is initialized.
    /// </summary>
    public bool IsInitialized => Volatile.Read(ref _initialized);

    /// <summary>
    /// Gets whether the SDK is ready (initialized and can evaluate flags).
    /// </summary>
    public bool IsReady => IsInitialized || _cache.Count > 0;

    /// <summary>
    /// Gets the current global context.
    /// </summary>
    public EvaluationContext GlobalContext => _contextManager.GlobalContext;

    /// <summary>
    /// Gets the last known server time from the most recent API response.
    /// </summary>
    public DateTime? LastServerTime => _lastServerTime;

    /// <summary>
    /// Gets the number of flags currently in cache.
    /// </summary>
    public int CachedFlagCount => _cache.Count;

    /// <summary>
    /// Initializes the SDK by fetching all flags from the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                var response = await _httpClient.GetAsync<InitResponse>("/sdk/init", cancellationToken);

                foreach (var flag in response.Flags)
                {
                    _cache.Set(flag.Key, flag);
                }

                if (!string.IsNullOrEmpty(response.Timestamp) &&
                    DateTime.TryParse(response.Timestamp, out var serverTime))
                {
                    _lastServerTime = serverTime;
                }

                Volatile.Write(ref _initialized, true);
                _readyTcs?.TrySetResult(true);

                _pollingManager.Start();
                _eventQueue?.Start();
            }
            catch (Exception ex)
            {
                _readyTcs?.TrySetException(ex);
                throw;
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    /// <summary>
    /// Waits for the SDK to be ready.
    /// </summary>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WaitForReadyAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        _readyTcs ??= new TaskCompletionSource<bool>();
        var tcs = _readyTcs;

        if (timeout.HasValue)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout.Value);

            try
            {
                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw FlagKitException.ConfigError(ErrorCode.SdkNotReady, "SDK initialization timed out");
            }
        }
        else
        {
            await tcs.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Forces a refresh of all flags from the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync<InitResponse>("/sdk/init", cancellationToken);

        foreach (var flag in response.Flags)
        {
            _cache.Set(flag.Key, flag);
        }

        if (!string.IsNullOrEmpty(response.Timestamp) &&
            DateTime.TryParse(response.Timestamp, out var serverTime))
        {
            _lastServerTime = serverTime;
        }
    }

    /// <summary>
    /// Identifies a user with optional attributes.
    /// </summary>
    /// <param name="userId">The user ID to identify.</param>
    /// <param name="attributes">Optional additional attributes.</param>
    public void Identify(string userId, Dictionary<string, object?>? attributes = null)
    {
        _contextManager.Identify(userId, attributes);
        _eventQueue?.TrackIdentify(userId, attributes);
    }

    /// <summary>
    /// Sets the global evaluation context.
    /// </summary>
    /// <param name="context">The context to set.</param>
    public void SetContext(EvaluationContext context)
    {
        _contextManager.SetContext(context);
    }

    /// <summary>
    /// Gets the current global evaluation context.
    /// </summary>
    /// <returns>The current global context.</returns>
    public EvaluationContext GetContext()
    {
        return _contextManager.GetContext();
    }

    /// <summary>
    /// Clears the global evaluation context.
    /// </summary>
    public void ClearContext()
    {
        _contextManager.ClearContext();
    }

    /// <summary>
    /// Resets the context to anonymous state.
    /// </summary>
    public void Reset()
    {
        _contextManager.Reset();
    }

    /// <summary>
    /// Evaluates a flag and returns the result from cache.
    /// </summary>
    /// <param name="flagKey">The flag key to evaluate.</param>
    /// <param name="context">Optional per-evaluation context.</param>
    /// <returns>The evaluation result.</returns>
    public EvaluationResult Evaluate(string flagKey, EvaluationContext? context = null)
    {
        var mergedContext = _contextManager.ResolveContext(context);
        var flag = _cache.Get(flagKey);

        if (flag == null)
        {
            return EvaluationResult.DefaultResult(
                flagKey,
                FlagValue.NullFlagValue.Instance,
                EvaluationReason.NotFound);
        }

        var result = new EvaluationResult
        {
            FlagKey = flagKey,
            Value = flag.Value,
            Enabled = flag.Enabled,
            Reason = EvaluationReason.Cached,
            Version = flag.Version
        };

        _eventQueue?.TrackEvaluation(flagKey, flag.Value.ToObject(), mergedContext);

        return result;
    }

    /// <summary>
    /// Evaluates a flag asynchronously, fetching from server if needed.
    /// </summary>
    /// <param name="flagKey">The flag key to evaluate.</param>
    /// <param name="context">Optional per-evaluation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The evaluation result.</returns>
    public async Task<EvaluationResult> EvaluateAsync(
        string flagKey,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var mergedContext = _contextManager.ResolveContext(context);

        try
        {
            var request = new EvaluateRequest
            {
                FlagKey = flagKey,
                Context = mergedContext.ToDictionary()
            };

            var response = await _httpClient.PostAsync<EvaluateRequest, EvaluateResponse>(
                "/sdk/evaluate",
                request,
                cancellationToken);

            var result = new EvaluationResult
            {
                FlagKey = response.FlagKey,
                Value = response.Value,
                Enabled = response.Enabled,
                Reason = response.Reason == default ? EvaluationReason.Server : response.Reason,
                Version = response.Version
            };

            _eventQueue?.TrackEvaluation(flagKey, response.Value.ToObject(), mergedContext);

            return result;
        }
        catch
        {
            // Fall back to cache
            return Evaluate(flagKey, context);
        }
    }

    /// <summary>
    /// Evaluates all flags and returns results.
    /// </summary>
    /// <param name="context">Optional per-evaluation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of flag keys to evaluation results.</returns>
    public async Task<Dictionary<string, EvaluationResult>> EvaluateAllAsync(
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var mergedContext = _contextManager.ResolveContext(context);

        try
        {
            var request = new EvaluateAllRequest
            {
                Context = mergedContext.ToDictionary()
            };

            var response = await _httpClient.PostAsync<EvaluateAllRequest, BatchEvaluateResponse>(
                "/sdk/evaluate/all",
                request,
                cancellationToken);

            var results = new Dictionary<string, EvaluationResult>();

            foreach (var (key, evalResponse) in response.Flags)
            {
                results[key] = new EvaluationResult
                {
                    FlagKey = evalResponse.FlagKey,
                    Value = evalResponse.Value,
                    Enabled = evalResponse.Enabled,
                    Reason = evalResponse.Reason == default ? EvaluationReason.Server : evalResponse.Reason,
                    Version = evalResponse.Version
                };

                // Update cache with server values
                _cache.Set(key, new FlagState
                {
                    Key = key,
                    Value = evalResponse.Value,
                    Enabled = evalResponse.Enabled,
                    Version = evalResponse.Version
                });
            }

            return results;
        }
        catch
        {
            // Fall back to cache
            var cached = _cache.GetAll();
            return cached.ToDictionary(
                kvp => kvp.Key,
                kvp => new EvaluationResult
                {
                    FlagKey = kvp.Key,
                    Value = kvp.Value.Value,
                    Enabled = kvp.Value.Enabled,
                    Reason = EvaluationReason.Cached,
                    Version = kvp.Value.Version
                });
        }
    }

    /// <summary>
    /// Evaluates multiple flags in a batch.
    /// </summary>
    /// <param name="flagKeys">The flag keys to evaluate.</param>
    /// <param name="context">Optional per-evaluation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of flag keys to evaluation results.</returns>
    public async Task<Dictionary<string, EvaluationResult>> EvaluateBatchAsync(
        IEnumerable<string> flagKeys,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var mergedContext = _contextManager.ResolveContext(context);
        var keys = flagKeys.ToList();

        try
        {
            var request = new BatchEvaluateRequest
            {
                FlagKeys = keys,
                Context = mergedContext.ToDictionary()
            };

            var response = await _httpClient.PostAsync<BatchEvaluateRequest, BatchEvaluateResponse>(
                "/sdk/evaluate/batch",
                request,
                cancellationToken);

            var results = new Dictionary<string, EvaluationResult>();

            foreach (var (key, evalResponse) in response.Flags)
            {
                results[key] = new EvaluationResult
                {
                    FlagKey = evalResponse.FlagKey,
                    Value = evalResponse.Value,
                    Enabled = evalResponse.Enabled,
                    Reason = evalResponse.Reason == default ? EvaluationReason.Server : evalResponse.Reason,
                    Version = evalResponse.Version
                };
            }

            return results;
        }
        catch
        {
            // Fall back to cache for requested keys
            var results = new Dictionary<string, EvaluationResult>();
            foreach (var key in keys)
            {
                results[key] = Evaluate(key, context);
            }
            return results;
        }
    }

    /// <summary>
    /// Gets a boolean flag value.
    /// </summary>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="defaultValue">Default value if flag not found.</param>
    /// <param name="context">Optional per-evaluation context.</param>
    /// <returns>The flag value or default.</returns>
    public bool GetBooleanValue(string flagKey, bool defaultValue, EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);
        return result.Reason == EvaluationReason.NotFound ? defaultValue : result.BoolValue;
    }

    /// <summary>
    /// Gets a string flag value.
    /// </summary>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="defaultValue">Default value if flag not found.</param>
    /// <param name="context">Optional per-evaluation context.</param>
    /// <returns>The flag value or default.</returns>
    public string GetStringValue(string flagKey, string defaultValue, EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);
        return result.Reason == EvaluationReason.NotFound ? defaultValue : result.StringValue ?? defaultValue;
    }

    /// <summary>
    /// Gets a numeric flag value.
    /// </summary>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="defaultValue">Default value if flag not found.</param>
    /// <param name="context">Optional per-evaluation context.</param>
    /// <returns>The flag value or default.</returns>
    public double GetNumberValue(string flagKey, double defaultValue, EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);
        return result.Reason == EvaluationReason.NotFound ? defaultValue : result.NumberValue;
    }

    /// <summary>
    /// Gets an integer flag value.
    /// </summary>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="defaultValue">Default value if flag not found.</param>
    /// <param name="context">Optional per-evaluation context.</param>
    /// <returns>The flag value or default.</returns>
    public long GetIntValue(string flagKey, long defaultValue, EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);
        return result.Reason == EvaluationReason.NotFound ? defaultValue : result.IntValue;
    }

    /// <summary>
    /// Gets a JSON flag value.
    /// </summary>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="defaultValue">Default value if flag not found.</param>
    /// <param name="context">Optional per-evaluation context.</param>
    /// <returns>The flag value or default.</returns>
    public Dictionary<string, object?>? GetJsonValue(
        string flagKey,
        Dictionary<string, object?>? defaultValue,
        EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);
        return result.Reason == EvaluationReason.NotFound ? defaultValue : result.JsonValue ?? defaultValue;
    }

    /// <summary>
    /// Gets a typed JSON flag value.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="flagKey">The flag key.</param>
    /// <param name="defaultValue">Default value if flag not found.</param>
    /// <param name="context">Optional per-evaluation context.</param>
    /// <returns>The flag value or default.</returns>
    public T GetJsonValue<T>(string flagKey, T defaultValue, EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);

        if (result.Reason == EvaluationReason.NotFound)
        {
            return defaultValue;
        }

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(result.Value.ToObject());
            var typed = System.Text.Json.JsonSerializer.Deserialize<T>(json);
            return typed ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Checks if a flag exists in the cache.
    /// </summary>
    /// <param name="flagKey">The flag key to check.</param>
    /// <returns>True if the flag exists.</returns>
    public bool HasFlag(string flagKey)
    {
        return _cache.Has(flagKey);
    }

    /// <summary>
    /// Gets all flag keys currently in cache.
    /// </summary>
    /// <returns>List of flag keys.</returns>
    public IReadOnlyList<string> GetAllFlagKeys()
    {
        return _cache.Keys.ToList();
    }

    /// <summary>
    /// Gets all cached flags.
    /// </summary>
    /// <returns>Dictionary of flag keys to flag states.</returns>
    public IReadOnlyDictionary<string, FlagState> GetAllFlags()
    {
        return _cache.GetAll();
    }

    /// <summary>
    /// Tracks a custom event.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <param name="data">Optional event data.</param>
    public void Track(string eventType, Dictionary<string, object?>? data = null)
    {
        _eventQueue?.TrackCustom(eventType, data);
    }

    /// <summary>
    /// Flushes pending events immediately.
    /// </summary>
    public async Task FlushAsync()
    {
        if (_eventQueue != null)
        {
            await _eventQueue.FlushAsync();
        }
    }

    private async Task PollForUpdatesAsync(DateTime? since)
    {
        var path = since.HasValue
            ? $"/sdk/updates?since={since.Value:O}"
            : "/sdk/updates";

        var response = await _httpClient.GetAsync<UpdatesResponse>(path);

        if (response.HasUpdates && response.Flags != null)
        {
            foreach (var flag in response.Flags)
            {
                _cache.Set(flag.Key, flag);
            }
        }

        if (!string.IsNullOrEmpty(response.CheckedAt) &&
            DateTime.TryParse(response.CheckedAt, out var checkedAt))
        {
            _lastServerTime = checkedAt;
        }
    }

    private async Task SendEventsAsync(List<AnalyticsEvent> events)
    {
        var request = new BatchEventsRequest { Events = events };
        await _httpClient.PostAsync("/sdk/events/batch", request);
    }

    private void LoadBootstrap(Dictionary<string, object> bootstrap)
    {
        foreach (var (key, value) in bootstrap)
        {
            var flag = new FlagState
            {
                Key = key,
                Value = FlagValue.From(value),
                Enabled = true,
                Version = 0
            };
            _cache.Set(key, flag);
        }
    }

    /// <summary>
    /// Disposes the client and releases all resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the client asynchronously, flushing any pending events.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose implementation.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _pollingManager.Dispose();
            _eventQueue?.Dispose();
            _httpClient.Dispose();
            _initializationLock.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// Protected async dispose implementation.
    /// </summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed) return;

        // Flush events before disposing
        if (_eventQueue != null)
        {
            try
            {
                await _eventQueue.FlushAsync();
            }
            catch
            {
                // Ignore flush errors during disposal
            }
        }

        _pollingManager.Dispose();
        _eventQueue?.Dispose();
        _httpClient.Dispose();
        _initializationLock.Dispose();

        _disposed = true;
    }
}
