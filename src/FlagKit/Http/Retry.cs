namespace FlagKit.Http;

/// <summary>
/// Configuration for retry behavior with exponential backoff.
/// </summary>
public record RetryConfig
{
    /// <summary>
    /// Maximum number of retry attempts. Default: 3
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Base delay in milliseconds. Default: 1000
    /// </summary>
    public int BaseDelayMs { get; init; } = 1000;

    /// <summary>
    /// Maximum delay in milliseconds. Default: 30000
    /// </summary>
    public int MaxDelayMs { get; init; } = 30000;

    /// <summary>
    /// Backoff multiplier. Default: 2
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Maximum jitter in milliseconds. Default: 250
    /// </summary>
    public int JitterMs { get; init; } = 250;

    /// <summary>
    /// Default retry configuration.
    /// </summary>
    public static RetryConfig Default { get; } = new();
}

/// <summary>
/// Result of a retry operation.
/// </summary>
/// <typeparam name="T">The result type.</typeparam>
public record RetryResult<T>
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The result value if successful.
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// The error if failed.
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// The number of attempts made.
    /// </summary>
    public required int Attempts { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static RetryResult<T> Succeeded(T value, int attempts) => new()
    {
        Success = true,
        Value = value,
        Attempts = attempts
    };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static RetryResult<T> Failed(Exception error, int attempts) => new()
    {
        Success = false,
        Error = error,
        Attempts = attempts
    };
}

/// <summary>
/// Provides retry functionality with exponential backoff and jitter.
/// </summary>
public static class Retry
{
    private static readonly Random Random = new();

    /// <summary>
    /// Executes an async operation with retry logic.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="config">The retry configuration.</param>
    /// <param name="shouldRetry">Optional predicate to determine if an error is retryable.</param>
    /// <param name="onRetry">Optional callback invoked before each retry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        RetryConfig? config = null,
        Func<Exception, bool>? shouldRetry = null,
        Action<int, Exception, TimeSpan>? onRetry = null,
        CancellationToken cancellationToken = default)
    {
        config ??= RetryConfig.Default;
        shouldRetry ??= IsRetryable;

        Exception? lastException = null;

        for (int attempt = 1; attempt <= config.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation();
            }
            catch (Exception ex) when (shouldRetry(ex) && attempt < config.MaxAttempts)
            {
                lastException = ex;
                var delay = CalculateBackoff(attempt, config);
                onRetry?.Invoke(attempt, ex, delay);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                throw;
            }
        }

        throw lastException ?? new InvalidOperationException("Retry failed without exception");
    }

    /// <summary>
    /// Executes an async operation with retry logic and returns a result wrapper.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="config">The retry configuration.</param>
    /// <param name="shouldRetry">Optional predicate to determine if an error is retryable.</param>
    /// <param name="onRetry">Optional callback invoked before each retry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The retry result wrapper.</returns>
    public static async Task<RetryResult<T>> TryExecuteAsync<T>(
        Func<Task<T>> operation,
        RetryConfig? config = null,
        Func<Exception, bool>? shouldRetry = null,
        Action<int, Exception, TimeSpan>? onRetry = null,
        CancellationToken cancellationToken = default)
    {
        config ??= RetryConfig.Default;
        shouldRetry ??= IsRetryable;

        Exception? lastException = null;
        int attempt = 0;

        for (attempt = 1; attempt <= config.MaxAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return RetryResult<T>.Failed(
                    new OperationCanceledException(cancellationToken),
                    attempt);
            }

            try
            {
                var result = await operation();
                return RetryResult<T>.Succeeded(result, attempt);
            }
            catch (Exception ex)
            {
                lastException = ex;

                if (!shouldRetry(ex) || attempt >= config.MaxAttempts)
                {
                    return RetryResult<T>.Failed(ex, attempt);
                }

                var delay = CalculateBackoff(attempt, config);
                onRetry?.Invoke(attempt, ex, delay);

                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return RetryResult<T>.Failed(
                        new OperationCanceledException(cancellationToken),
                        attempt);
                }
            }
        }

        return RetryResult<T>.Failed(
            lastException ?? new InvalidOperationException("Retry failed"),
            attempt);
    }

    /// <summary>
    /// Executes a synchronous operation with retry logic.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="config">The retry configuration.</param>
    /// <param name="shouldRetry">Optional predicate to determine if an error is retryable.</param>
    /// <param name="onRetry">Optional callback invoked before each retry.</param>
    /// <returns>The operation result.</returns>
    public static T Execute<T>(
        Func<T> operation,
        RetryConfig? config = null,
        Func<Exception, bool>? shouldRetry = null,
        Action<int, Exception, TimeSpan>? onRetry = null)
    {
        config ??= RetryConfig.Default;
        shouldRetry ??= IsRetryable;

        Exception? lastException = null;

        for (int attempt = 1; attempt <= config.MaxAttempts; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception ex) when (shouldRetry(ex) && attempt < config.MaxAttempts)
            {
                lastException = ex;
                var delay = CalculateBackoff(attempt, config);
                onRetry?.Invoke(attempt, ex, delay);
                Thread.Sleep(delay);
            }
            catch (Exception ex)
            {
                lastException = ex;
                throw;
            }
        }

        throw lastException ?? new InvalidOperationException("Retry failed without exception");
    }

    /// <summary>
    /// Calculates the backoff delay for a given attempt using exponential backoff with jitter.
    /// </summary>
    /// <param name="attempt">The current attempt number (1-based).</param>
    /// <param name="config">The retry configuration.</param>
    /// <returns>The delay to wait before the next attempt.</returns>
    public static TimeSpan CalculateBackoff(int attempt, RetryConfig config)
    {
        // Exponential backoff: baseDelay * (multiplier ^ (attempt - 1))
        var exponentialDelay = config.BaseDelayMs * Math.Pow(config.BackoffMultiplier, attempt - 1);

        // Cap at maxDelay
        var cappedDelay = Math.Min(exponentialDelay, config.MaxDelayMs);

        // Add jitter to prevent thundering herd
        double jitter;
        lock (Random)
        {
            jitter = Random.NextDouble() * config.JitterMs;
        }

        return TimeSpan.FromMilliseconds(cappedDelay + jitter);
    }

    /// <summary>
    /// Determines if an exception is retryable.
    /// </summary>
    /// <param name="ex">The exception to check.</param>
    /// <returns>True if the exception is retryable.</returns>
    public static bool IsRetryable(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => true,
            TaskCanceledException { InnerException: TimeoutException } => true,
            TimeoutException => true,
            Errors.FlagKitException fke => fke.ErrorCode switch
            {
                Errors.ErrorCode.HttpTimeout => true,
                Errors.ErrorCode.HttpNetworkError => true,
                Errors.ErrorCode.HttpServerError => true,
                Errors.ErrorCode.HttpRateLimited => true,
                Errors.ErrorCode.NetworkError => true,
                Errors.ErrorCode.NetworkTimeout => true,
                _ => false
            },
            _ => false
        };
    }

    /// <summary>
    /// Parses a Retry-After header value.
    /// </summary>
    /// <param name="value">The header value.</param>
    /// <returns>The delay in seconds, or null if parsing failed.</returns>
    public static int? ParseRetryAfter(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        // Try parsing as number of seconds
        if (int.TryParse(value, out var seconds) && seconds > 0)
            return seconds;

        // Try parsing as HTTP date
        if (DateTime.TryParse(value, out var date))
        {
            var now = DateTime.UtcNow;
            if (date > now)
            {
                return (int)Math.Ceiling((date - now).TotalSeconds);
            }
        }

        return null;
    }
}
