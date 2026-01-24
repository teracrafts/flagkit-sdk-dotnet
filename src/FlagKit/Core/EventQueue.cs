using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using FlagKit.Types;

namespace FlagKit.Core;

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
/// Thread-safe with CancellationToken support for graceful shutdown.
/// </summary>
public class EventQueue : IDisposable, IAsyncDisposable
{
    private readonly ConcurrentQueue<AnalyticsEvent> _queue = new();
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly Func<List<AnalyticsEvent>, Task> _onFlush;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly CancellationTokenSource _disposalCts = new();

    private Timer? _flushTimer;
    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// Creates a new event queue.
    /// </summary>
    /// <param name="batchSize">Maximum events per batch (default: 10).</param>
    /// <param name="flushInterval">Time between automatic flushes (default: 30 seconds).</param>
    /// <param name="onFlush">Callback to send events to the server.</param>
    public EventQueue(
        int batchSize,
        TimeSpan flushInterval,
        Func<List<AnalyticsEvent>, Task> onFlush)
    {
        _batchSize = batchSize > 0 ? batchSize : 10;
        _flushInterval = flushInterval > TimeSpan.Zero ? flushInterval : TimeSpan.FromSeconds(30);
        _onFlush = onFlush ?? throw new ArgumentNullException(nameof(onFlush));
    }

    /// <summary>
    /// Gets the number of events currently in the queue.
    /// </summary>
    public int Count => _queue.Count;

    /// <summary>
    /// Gets whether the queue is running (auto-flushing enabled).
    /// </summary>
    public bool IsRunning => Volatile.Read(ref _isRunning);

    /// <summary>
    /// Starts the auto-flush timer.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;

        if (Volatile.Read(ref _isRunning)) return;

        Volatile.Write(ref _isRunning, true);
        _flushTimer = new Timer(
            OnTimerCallback,
            null,
            _flushInterval,
            _flushInterval);
    }

    /// <summary>
    /// Stops the auto-flush timer.
    /// </summary>
    public void Stop()
    {
        Volatile.Write(ref _isRunning, false);
        _flushTimer?.Dispose();
        _flushTimer = null;
    }

    /// <summary>
    /// Adds an event to the queue.
    /// Triggers an immediate flush if the batch size is reached.
    /// </summary>
    /// <param name="evt">The event to add.</param>
    public void Enqueue(AnalyticsEvent evt)
    {
        if (_disposed) return;

        _queue.Enqueue(evt);

        // Trigger flush if batch size reached
        if (_queue.Count >= _batchSize)
        {
            _ = FlushAsync();
        }
    }

    /// <summary>
    /// Tracks a flag evaluation event.
    /// </summary>
    /// <param name="flagKey">The flag key that was evaluated.</param>
    /// <param name="value">The evaluated value.</param>
    /// <param name="context">The evaluation context.</param>
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

    /// <summary>
    /// Tracks a custom event.
    /// </summary>
    /// <param name="eventType">The custom event type.</param>
    /// <param name="data">Optional event data.</param>
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

    /// <summary>
    /// Tracks a user identification event.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="attributes">Optional user attributes.</param>
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

    /// <summary>
    /// Flushes pending events immediately.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        // Use a linked token that respects both the passed token and disposal
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposalCts.Token);

        // Try to acquire the flush lock with timeout
        if (!await _flushLock.WaitAsync(TimeSpan.FromSeconds(5), linkedCts.Token))
        {
            return; // Another flush is in progress
        }

        try
        {
            await FlushInternalAsync(linkedCts.Token);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>
    /// Flushes all remaining events, ignoring errors.
    /// Used during disposal.
    /// </summary>
    public async Task FlushAllAsync()
    {
        if (_queue.IsEmpty) return;

        try
        {
            await _flushLock.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                // Flush all events, not just a batch
                var events = new List<AnalyticsEvent>();
                while (_queue.TryDequeue(out var evt))
                {
                    events.Add(evt);
                }

                if (events.Count > 0)
                {
                    await _onFlush(events);
                }
            }
            finally
            {
                _flushLock.Release();
            }
        }
        catch
        {
            // Ignore errors during final flush
        }
    }

    private async Task FlushInternalAsync(CancellationToken cancellationToken)
    {
        var events = new List<AnalyticsEvent>();

        while (events.Count < _batchSize && _queue.TryDequeue(out var evt))
        {
            events.Add(evt);
        }

        if (events.Count == 0) return;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _onFlush(events);
        }
        catch (OperationCanceledException)
        {
            // Re-queue events on cancellation
            foreach (var evt in events)
            {
                _queue.Enqueue(evt);
            }
            throw;
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

    private void OnTimerCallback(object? state)
    {
        if (!_isRunning || _disposed) return;

        try
        {
            _ = FlushAsync(_disposalCts.Token);
        }
        catch
        {
            // Ignore timer callback errors
        }
    }

    /// <summary>
    /// Disposes the event queue, stopping the timer and flushing remaining events.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the event queue asynchronously, flushing all remaining events.
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
            Volatile.Write(ref _isRunning, false);
            _disposalCts.Cancel();
            _flushTimer?.Dispose();
            _flushTimer = null;
            _flushLock.Dispose();
            _disposalCts.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// Protected async dispose implementation.
    /// </summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed) return;

        Volatile.Write(ref _isRunning, false);
        _flushTimer?.Dispose();
        _flushTimer = null;

        // Flush remaining events before disposal
        await FlushAllAsync();

        _disposalCts.Cancel();
        _flushLock.Dispose();
        _disposalCts.Dispose();

        _disposed = true;
    }
}
