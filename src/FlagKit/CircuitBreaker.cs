namespace FlagKit;

/// <summary>
/// Circuit breaker states.
/// </summary>
public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// Thread-safe circuit breaker for fault tolerance.
/// </summary>
public class CircuitBreaker
{
    private readonly int _threshold;
    private readonly TimeSpan _resetTimeout;
    private readonly object _lock = new();

    private CircuitState _state = CircuitState.Closed;
    private int _failureCount;
    private DateTime? _openedAt;

    public CircuitBreaker(int threshold = 5, TimeSpan? resetTimeout = null)
    {
        _threshold = threshold;
        _resetTimeout = resetTimeout ?? TimeSpan.FromSeconds(30);
    }

    public CircuitState State
    {
        get
        {
            lock (_lock)
            {
                if (_state == CircuitState.Open && _openedAt.HasValue)
                {
                    if (DateTime.UtcNow - _openedAt.Value >= _resetTimeout)
                    {
                        _state = CircuitState.HalfOpen;
                    }
                }
                return _state;
            }
        }
    }

    public bool IsOpen => State == CircuitState.Open;
    public bool IsClosed => State == CircuitState.Closed;
    public bool IsHalfOpen => State == CircuitState.HalfOpen;

    public int FailureCount
    {
        get
        {
            lock (_lock)
            {
                return _failureCount;
            }
        }
    }

    public bool CanExecute()
    {
        var state = State;
        return state == CircuitState.Closed || state == CircuitState.HalfOpen;
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failureCount = 0;
            _state = CircuitState.Closed;
            _openedAt = null;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;

            if (_state == CircuitState.HalfOpen || _failureCount >= _threshold)
            {
                _state = CircuitState.Open;
                _openedAt = DateTime.UtcNow;
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitState.Closed;
            _failureCount = 0;
            _openedAt = null;
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, Func<T>? fallback = null)
    {
        if (!CanExecute())
        {
            if (fallback != null)
                return fallback();

            throw FlagKitException.NetworkError(
                ErrorCode.HttpCircuitOpen,
                "Circuit breaker is open");
        }

        try
        {
            var result = await action();
            RecordSuccess();
            return result;
        }
        catch (Exception ex) when (ex is not FlagKitException { Code: ErrorCode.HttpCircuitOpen })
        {
            RecordFailure();
            throw;
        }
    }

    public T Execute<T>(Func<T> action, Func<T>? fallback = null)
    {
        if (!CanExecute())
        {
            if (fallback != null)
                return fallback();

            throw FlagKitException.NetworkError(
                ErrorCode.HttpCircuitOpen,
                "Circuit breaker is open");
        }

        try
        {
            var result = action();
            RecordSuccess();
            return result;
        }
        catch (Exception ex) when (ex is not FlagKitException { Code: ErrorCode.HttpCircuitOpen })
        {
            RecordFailure();
            throw;
        }
    }
}
