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
}

/// <summary>
/// Main FlagKit client for feature flag evaluation.
/// </summary>
public class FlagKitClient : IDisposable
{
    private readonly FlagKitOptions _options;
    private readonly FlagKitHttpClient _httpClient;
    private readonly FlagCache _cache;
    private readonly PollingManager _pollingManager;
    private readonly EventQueue? _eventQueue;
    private readonly object _lock = new();

    private EvaluationContext _globalContext = new();
    private bool _initialized;
    private bool _disposed;
    private TaskCompletionSource<bool>? _readyTcs;

    public FlagKitClient(FlagKitOptions options)
    {
        options.Validate();
        _options = options;

        _httpClient = new FlagKitHttpClient(options);
        _cache = new FlagCache(options.MaxCacheSize, options.CacheTtl);

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

    public bool IsInitialized
    {
        get
        {
            lock (_lock)
            {
                return _initialized;
            }
        }
    }

    public EvaluationContext GlobalContext
    {
        get
        {
            lock (_lock)
            {
                return _globalContext;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync<InitResponse>("/sdk/init", cancellationToken);

            foreach (var flag in response.Flags)
            {
                _cache.Set(flag.Key, flag);
            }

            lock (_lock)
            {
                _initialized = true;
                _readyTcs?.TrySetResult(true);
            }

            _pollingManager.Start();
            _eventQueue?.Start();
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _readyTcs?.TrySetException(ex);
            }
            throw;
        }
    }

    public async Task WaitForReadyAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool> tcs;

        lock (_lock)
        {
            if (_initialized) return;

            _readyTcs ??= new TaskCompletionSource<bool>();
            tcs = _readyTcs;
        }

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

    public void Identify(string userId, Dictionary<string, object?>? attributes = null)
    {
        lock (_lock)
        {
            _globalContext = _globalContext.WithUserId(userId);

            if (attributes != null)
            {
                _globalContext = _globalContext.WithAttributes(attributes);
            }
        }

        _eventQueue?.TrackIdentify(userId, attributes);
    }

    public void SetContext(EvaluationContext context)
    {
        lock (_lock)
        {
            _globalContext = context;
        }
    }

    public void ClearContext()
    {
        lock (_lock)
        {
            _globalContext = new EvaluationContext();
        }
    }

    public EvaluationResult Evaluate(string flagKey, EvaluationContext? context = null)
    {
        var mergedContext = MergeContext(context);
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

        _eventQueue?.TrackEvaluation(flagKey, flag.Value.ToObject(), mergedContext.StripPrivateAttributes());

        return result;
    }

    public async Task<EvaluationResult> EvaluateAsync(
        string flagKey,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var mergedContext = MergeContext(context);

        try
        {
            var request = new EvaluateRequest
            {
                FlagKey = flagKey,
                Context = mergedContext.StripPrivateAttributes().ToDictionary()
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
                Reason = response.Reason,
                Version = response.Version
            };

            _eventQueue?.TrackEvaluation(flagKey, response.Value.ToObject(), mergedContext.StripPrivateAttributes());

            return result;
        }
        catch
        {
            // Fall back to cache
            return Evaluate(flagKey, context);
        }
    }

    public bool GetBooleanValue(string flagKey, bool defaultValue, EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);
        return result.Reason == EvaluationReason.NotFound ? defaultValue : result.BoolValue;
    }

    public string GetStringValue(string flagKey, string defaultValue, EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);
        return result.Reason == EvaluationReason.NotFound ? defaultValue : result.StringValue ?? defaultValue;
    }

    public double GetNumberValue(string flagKey, double defaultValue, EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);
        return result.Reason == EvaluationReason.NotFound ? defaultValue : result.NumberValue;
    }

    public long GetIntValue(string flagKey, long defaultValue, EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);
        return result.Reason == EvaluationReason.NotFound ? defaultValue : result.IntValue;
    }

    public Dictionary<string, object?>? GetJsonValue(string flagKey, Dictionary<string, object?>? defaultValue, EvaluationContext? context = null)
    {
        var result = Evaluate(flagKey, context);
        return result.Reason == EvaluationReason.NotFound ? defaultValue : result.JsonValue ?? defaultValue;
    }

    public IReadOnlyDictionary<string, FlagState> GetAllFlags()
    {
        return _cache.GetAll();
    }

    public void Track(string eventType, Dictionary<string, object?>? data = null)
    {
        _eventQueue?.TrackCustom(eventType, data);
    }

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
    }

    private async Task SendEventsAsync(List<AnalyticsEvent> events)
    {
        var request = new BatchEventsRequest { Events = events };
        await _httpClient.PostAsync("/sdk/events/batch", request);
    }

    private EvaluationContext MergeContext(EvaluationContext? context)
    {
        EvaluationContext global;
        lock (_lock)
        {
            global = _globalContext;
        }

        return global.Merge(context);
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

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _pollingManager.Dispose();
        _eventQueue?.Dispose();
        _httpClient.Dispose();
    }
}
