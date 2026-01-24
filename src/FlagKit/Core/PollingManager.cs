namespace FlagKit.Core;

/// <summary>
/// Manages background polling for flag updates with exponential backoff on failures.
/// Thread-safe with CancellationToken support for graceful shutdown.
/// </summary>
public class PollingManager : IDisposable, IAsyncDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeSpan _maxInterval;
    private readonly Func<DateTime?, Task> _onPoll;
    private readonly Random _random = new();
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    private Timer? _timer;
    private DateTime? _lastUpdate;
    private int _consecutiveFailures;
    private bool _isPolling;
    private bool _disposed;

    private const int MaxBackoffMultiplier = 8;
    private const double JitterFactor = 0.1;

    /// <summary>
    /// Creates a new polling manager.
    /// </summary>
    /// <param name="interval">Base polling interval (default: 30 seconds).</param>
    /// <param name="onPoll">Callback to fetch updates from the server.</param>
    /// <param name="maxInterval">Maximum polling interval with backoff (default: 5 minutes).</param>
    public PollingManager(
        TimeSpan interval,
        Func<DateTime?, Task> onPoll,
        TimeSpan? maxInterval = null)
    {
        _interval = interval > TimeSpan.Zero ? interval : TimeSpan.FromSeconds(30);
        _maxInterval = maxInterval ?? TimeSpan.FromMinutes(5);
        _onPoll = onPoll ?? throw new ArgumentNullException(nameof(onPoll));
    }

    /// <summary>
    /// Gets whether polling is currently active.
    /// </summary>
    public bool IsPolling => Volatile.Read(ref _isPolling);

    /// <summary>
    /// Gets the timestamp of the last successful update.
    /// </summary>
    public DateTime? LastUpdate
    {
        get
        {
            lock (_random) // Use _random as a simple lock
            {
                return _lastUpdate;
            }
        }
    }

    /// <summary>
    /// Gets the current number of consecutive failures.
    /// </summary>
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <summary>
    /// Starts background polling.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;
        if (Volatile.Read(ref _isPolling)) return;

        Volatile.Write(ref _isPolling, true);
        ScheduleNextPoll(_interval);
    }

    /// <summary>
    /// Stops background polling.
    /// </summary>
    public void Stop()
    {
        Volatile.Write(ref _isPolling, false);
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Triggers an immediate poll, bypassing the timer.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PollNowAsync(CancellationToken cancellationToken = default)
    {
        await ExecutePollAsync(cancellationToken);
    }

    private void ScheduleNextPoll(TimeSpan delay)
    {
        if (!_isPolling || _disposed) return;

        _timer?.Dispose();
        _timer = new Timer(
            OnTimerCallback,
            null,
            delay,
            Timeout.InfiniteTimeSpan);
    }

    private void OnTimerCallback(object? state)
    {
        if (!_isPolling || _disposed) return;

        _ = ExecutePollWithBackoffAsync();
    }

    private async Task ExecutePollWithBackoffAsync()
    {
        try
        {
            await ExecutePollAsync(_disposalCts.Token);

            // Reset failures on success
            Volatile.Write(ref _consecutiveFailures, 0);
            ScheduleNextPoll(_interval);
        }
        catch (OperationCanceledException)
        {
            // Cancelled, don't reschedule
        }
        catch
        {
            // Increment failures and apply backoff
            Interlocked.Increment(ref _consecutiveFailures);
            var nextDelay = CalculateBackoffInterval();
            ScheduleNextPoll(nextDelay);
        }
    }

    private async Task ExecutePollAsync(CancellationToken cancellationToken)
    {
        // Prevent concurrent polls
        if (!await _pollLock.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken))
        {
            return;
        }

        try
        {
            DateTime? since;
            lock (_random)
            {
                since = _lastUpdate;
            }

            await _onPoll(since);

            lock (_random)
            {
                _lastUpdate = DateTime.UtcNow;
            }
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private TimeSpan CalculateBackoffInterval()
    {
        var failures = Volatile.Read(ref _consecutiveFailures);

        // Exponential backoff: interval * 2^(failures-1), capped at MaxBackoffMultiplier
        var multiplier = Math.Min(Math.Pow(2, failures - 1), MaxBackoffMultiplier);
        var baseDelay = _interval.TotalMilliseconds * multiplier;

        // Cap at max interval
        baseDelay = Math.Min(baseDelay, _maxInterval.TotalMilliseconds);

        // Add jitter (random variance of +/- JitterFactor)
        double jitter;
        lock (_random)
        {
            jitter = baseDelay * JitterFactor * (_random.NextDouble() * 2 - 1);
        }
        var delay = baseDelay + jitter;

        // Ensure minimum delay is the base interval
        return TimeSpan.FromMilliseconds(Math.Max(delay, _interval.TotalMilliseconds));
    }

    /// <summary>
    /// Disposes the polling manager.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the polling manager asynchronously.
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
            Volatile.Write(ref _isPolling, false);
            _disposalCts.Cancel();
            _timer?.Dispose();
            _timer = null;
            _pollLock.Dispose();
            _disposalCts.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// Protected async dispose implementation.
    /// </summary>
    protected virtual ValueTask DisposeAsyncCore()
    {
        if (_disposed) return ValueTask.CompletedTask;

        Volatile.Write(ref _isPolling, false);
        _disposalCts.Cancel();
        _timer?.Dispose();
        _timer = null;
        _pollLock.Dispose();
        _disposalCts.Dispose();

        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
