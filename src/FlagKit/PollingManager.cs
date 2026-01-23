namespace FlagKit;

/// <summary>
/// Manages background polling for flag updates.
/// </summary>
public class PollingManager : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Func<DateTime?, Task> _onPoll;
    private readonly Random _random = new();
    private readonly object _lock = new();

    private Timer? _timer;
    private DateTime? _lastUpdate;
    private int _consecutiveFailures;
    private bool _isPolling;
    private bool _disposed;

    private const int MaxBackoffMultiplier = 8;
    private const double JitterFactor = 0.1;

    public PollingManager(TimeSpan interval, Func<DateTime?, Task> onPoll)
    {
        _interval = interval;
        _onPoll = onPoll;
    }

    public bool IsPolling
    {
        get
        {
            lock (_lock)
            {
                return _isPolling;
            }
        }
    }

    public DateTime? LastUpdate
    {
        get
        {
            lock (_lock)
            {
                return _lastUpdate;
            }
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isPolling || _disposed) return;

            _isPolling = true;
            ScheduleNextPoll(_interval);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isPolling = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    public async Task PollNowAsync()
    {
        await ExecutePollAsync();
    }

    private void ScheduleNextPoll(TimeSpan delay)
    {
        lock (_lock)
        {
            if (!_isPolling || _disposed) return;

            _timer?.Dispose();
            _timer = new Timer(
                async _ => await OnTimerElapsedAsync(),
                null,
                delay,
                Timeout.InfiniteTimeSpan);
        }
    }

    private async Task OnTimerElapsedAsync()
    {
        try
        {
            await ExecutePollAsync();

            lock (_lock)
            {
                _consecutiveFailures = 0;
            }

            ScheduleNextPoll(_interval);
        }
        catch
        {
            var nextDelay = CalculateBackoffInterval();
            ScheduleNextPoll(nextDelay);
        }
    }

    private async Task ExecutePollAsync()
    {
        DateTime? since;
        lock (_lock)
        {
            since = _lastUpdate;
        }

        try
        {
            await _onPoll(since);

            lock (_lock)
            {
                _lastUpdate = DateTime.UtcNow;
            }
        }
        catch
        {
            lock (_lock)
            {
                _consecutiveFailures++;
            }
            throw;
        }
    }

    private TimeSpan CalculateBackoffInterval()
    {
        int failures;
        lock (_lock)
        {
            failures = _consecutiveFailures;
        }

        var multiplier = Math.Min(Math.Pow(2, failures - 1), MaxBackoffMultiplier);
        var baseDelay = _interval.TotalMilliseconds * multiplier;

        // Add jitter
        var jitter = baseDelay * JitterFactor * (_random.NextDouble() * 2 - 1);
        var delay = baseDelay + jitter;

        return TimeSpan.FromMilliseconds(Math.Max(delay, _interval.TotalMilliseconds));
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _disposed = true;
            _isPolling = false;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
