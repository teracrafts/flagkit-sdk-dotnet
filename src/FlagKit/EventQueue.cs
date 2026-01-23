using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace FlagKit;

/// <summary>
/// Event types for analytics.
/// </summary>
public enum EventType
{
    [JsonPropertyName("evaluation")]
    Evaluation,

    [JsonPropertyName("custom")]
    Custom,

    [JsonPropertyName("identify")]
    Identify
}

/// <summary>
/// Analytics event data.
/// </summary>
public record AnalyticsEvent
{
    [JsonPropertyName("eventType")]
    public required EventType EventType { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("flagKey")]
    public string? FlagKey { get; init; }

    [JsonPropertyName("value")]
    public object? Value { get; init; }

    [JsonPropertyName("context")]
    public Dictionary<string, object?>? Context { get; init; }

    [JsonPropertyName("data")]
    public Dictionary<string, object?>? Data { get; init; }
}

/// <summary>
/// Request body for batch events endpoint.
/// </summary>
public record BatchEventsRequest
{
    [JsonPropertyName("events")]
    public required List<AnalyticsEvent> Events { get; init; }
}

/// <summary>
/// Manages batching and sending of analytics events.
/// </summary>
public class EventQueue : IDisposable
{
    private readonly ConcurrentQueue<AnalyticsEvent> _queue = new();
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly Func<List<AnalyticsEvent>, Task> _onFlush;
    private readonly object _lock = new();

    private Timer? _flushTimer;
    private bool _isRunning;
    private bool _disposed;

    public EventQueue(
        int batchSize,
        TimeSpan flushInterval,
        Func<List<AnalyticsEvent>, Task> onFlush)
    {
        _batchSize = batchSize;
        _flushInterval = flushInterval;
        _onFlush = onFlush;
    }

    public int Count => _queue.Count;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _isRunning;
            }
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning || _disposed) return;

            _isRunning = true;
            _flushTimer = new Timer(
                async _ => await FlushAsync(),
                null,
                _flushInterval,
                _flushInterval);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isRunning = false;
            _flushTimer?.Dispose();
            _flushTimer = null;
        }
    }

    public void Enqueue(AnalyticsEvent evt)
    {
        if (_disposed) return;

        _queue.Enqueue(evt);

        if (_queue.Count >= _batchSize)
        {
            _ = FlushAsync();
        }
    }

    public void TrackEvaluation(
        string flagKey,
        object? value,
        EvaluationContext? context = null)
    {
        Enqueue(new AnalyticsEvent
        {
            EventType = EventType.Evaluation,
            FlagKey = flagKey,
            Value = value,
            Context = context?.ToDictionary()
        });
    }

    public void TrackCustom(string eventType, Dictionary<string, object?>? data = null)
    {
        Enqueue(new AnalyticsEvent
        {
            EventType = EventType.Custom,
            Data = new Dictionary<string, object?>
            {
                ["eventType"] = eventType,
                ["data"] = data
            }
        });
    }

    public void TrackIdentify(string userId, Dictionary<string, object?>? attributes = null)
    {
        Enqueue(new AnalyticsEvent
        {
            EventType = EventType.Identify,
            Context = new Dictionary<string, object?>
            {
                ["userId"] = userId,
                ["attributes"] = attributes
            }
        });
    }

    public async Task FlushAsync()
    {
        var events = new List<AnalyticsEvent>();

        while (events.Count < _batchSize && _queue.TryDequeue(out var evt))
        {
            events.Add(evt);
        }

        if (events.Count == 0) return;

        try
        {
            await _onFlush(events);
        }
        catch
        {
            // Re-queue events on failure
            foreach (var evt in events)
            {
                _queue.Enqueue(evt);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _disposed = true;
            _isRunning = false;
            _flushTimer?.Dispose();
            _flushTimer = null;
        }

        // Final flush
        _ = FlushAsync();
    }
}
