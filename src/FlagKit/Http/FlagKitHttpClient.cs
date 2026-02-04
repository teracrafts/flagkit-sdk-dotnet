using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlagKit.Errors;
using FlagKit.Utils;

namespace FlagKit.Http;

/// <summary>
/// Usage metrics extracted from response headers.
/// </summary>
public record UsageMetrics
{
    /// <summary>
    /// Percentage of API call limit used this period (0-150+).
    /// </summary>
    public double? ApiUsagePercent { get; init; }

    /// <summary>
    /// Percentage of evaluation limit used (0-150+).
    /// </summary>
    public double? EvaluationUsagePercent { get; init; }

    /// <summary>
    /// Whether approaching rate limit threshold.
    /// </summary>
    public bool RateLimitWarning { get; init; }

    /// <summary>
    /// Current subscription status.
    /// </summary>
    public string? SubscriptionStatus { get; init; }
}

/// <summary>
/// Callback type for usage metrics updates.
/// </summary>
public delegate void UsageUpdateCallback(UsageMetrics metrics);

/// <summary>
/// HTTP client with retry logic, circuit breaker, request signing, and key rotation.
/// </summary>
public class FlagKitHttpClient : IDisposable
{
    internal const string DefaultBaseUrl = "https://api.flagkit.dev/api/v1";

    private static readonly string[] ValidSubscriptionStatuses = { "active", "trial", "past_due", "suspended", "cancelled" };

    private readonly HttpClient _httpClient;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly FlagKitOptions _options;
    private readonly Random _random = new();
    private bool _disposed;
    private bool _usingSecondaryKey = false;
    private readonly object _keyRotationLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Returns the base URL for the given local port, or the default production URL.
    /// </summary>
    public static string GetBaseUrl(int? localPort) =>
        localPort.HasValue ? $"http://localhost:{localPort.Value}/api/v1" : DefaultBaseUrl;

    public FlagKitHttpClient(FlagKitOptions options)
    {
        _options = options;
        _circuitBreaker = new CircuitBreaker(
            options.CircuitBreakerThreshold,
            options.CircuitBreakerResetTimeout);

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(GetBaseUrl(options.LocalPort)),
            Timeout = options.Timeout
        };

        // Note: API key is now added per-request to support key rotation
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"FlagKit-DotNet/{GetVersion()}");
        _httpClient.DefaultRequestHeaders.Add("X-FlagKit-SDK-Version", GetVersion());
        _httpClient.DefaultRequestHeaders.Add("X-FlagKit-SDK-Language", "dotnet");
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAndKeyRotationAsync(async (apiKey) =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-API-Key", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return await HandleResponseWithMetricsAsync<T>(response, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Gets the currently active API key (primary or secondary if rotated).
    /// </summary>
    public string CurrentApiKey
    {
        get
        {
            lock (_keyRotationLock)
            {
                return _usingSecondaryKey && !string.IsNullOrEmpty(_options.SecondaryApiKey)
                    ? _options.SecondaryApiKey
                    : _options.ApiKey;
            }
        }
    }

    /// <summary>
    /// Gets whether the client is currently using the secondary API key.
    /// </summary>
    public bool IsUsingSecondaryKey
    {
        get
        {
            lock (_keyRotationLock)
            {
                return _usingSecondaryKey;
            }
        }
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAndKeyRotationAsync(async (apiKey) =>
        {
            var request = CreateSignedPostRequest(path, body, apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return await HandleResponseWithMetricsAsync<TResponse>(response, cancellationToken);
        }, cancellationToken);
    }

    public async Task PostAsync<TRequest>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAndKeyRotationAsync(async (apiKey) =>
        {
            var request = CreateSignedPostRequest(path, body, apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessWithMetricsAsync(response, cancellationToken);
            return true;
        }, cancellationToken);
    }

    private HttpRequestMessage CreateSignedPostRequest<TRequest>(
        string path,
        TRequest body,
        string apiKey)
    {
        var jsonBody = JsonSerializer.Serialize(body, JsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        // Add API key header
        request.Headers.Add("X-API-Key", apiKey);

        // Add request signing if enabled
        if (_options.EnableRequestSigning)
        {
            var (signature, timestamp) = Security.CreateRequestSignature(jsonBody, apiKey);
            request.Headers.Add("X-Signature", signature);
            request.Headers.Add("X-Timestamp", timestamp.ToString());
            request.Headers.Add("X-Key-Id", Security.GetKeyId(apiKey));
        }

        return request;
    }

    private async Task<T> ExecuteWithRetryAndKeyRotationAsync<T>(
        Func<string, Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteWithRetryAsync(
                () => action(CurrentApiKey),
                cancellationToken);
        }
        catch (FlagKitException ex) when (ex.Code == ErrorCode.HttpUnauthorized)
        {
            // Try key rotation if we have a secondary key and haven't already rotated
            if (!string.IsNullOrEmpty(_options.SecondaryApiKey))
            {
                bool shouldRetry;
                lock (_keyRotationLock)
                {
                    if (!_usingSecondaryKey)
                    {
                        _usingSecondaryKey = true;
                        shouldRetry = true;
                    }
                    else
                    {
                        shouldRetry = false;
                    }
                }

                if (shouldRetry)
                {
                    // Retry with the secondary key
                    return await ExecuteWithRetryAsync(
                        () => action(CurrentApiKey),
                        cancellationToken);
                }
            }

            // No secondary key or already tried - rethrow
            throw;
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        return await _circuitBreaker.ExecuteAsync(async () =>
        {
            Exception? lastException = null;

            for (int attempt = 0; attempt <= _options.RetryAttempts; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex) when (IsRetryable(ex) && attempt < _options.RetryAttempts)
                {
                    lastException = ex;
                    var delay = CalculateBackoff(attempt);
                    await Task.Delay(delay, cancellationToken);
                }
            }

            throw lastException ?? new InvalidOperationException("Retry failed without exception");
        });
    }

    private static bool IsRetryable(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => true,
            TaskCanceledException { InnerException: TimeoutException } => true,
            FlagKitException fke => fke.Code switch
            {
                ErrorCode.HttpTimeout => true,
                ErrorCode.HttpNetworkError => true,
                ErrorCode.HttpServerError => true,
                _ => false
            },
            _ => false
        };
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        const double baseDelay = 1000;
        const double maxDelay = 30000;
        const double multiplier = 2.0;

        var delay = baseDelay * Math.Pow(multiplier, attempt);
        delay = Math.Min(delay, maxDelay);

        // Add jitter (0-25%)
        var jitter = delay * 0.25 * _random.NextDouble();
        delay += jitter;

        return TimeSpan.FromMilliseconds(delay);
    }

    private async Task<T> HandleResponseWithMetricsAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessWithMetricsAsync(response, cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            var result = JsonSerializer.Deserialize<T>(content, JsonOptions);
            return result ?? throw FlagKitException.NetworkError(
                ErrorCode.HttpInvalidResponse,
                "Response deserialized to null");
        }
        catch (JsonException ex)
        {
            throw FlagKitException.NetworkError(
                ErrorCode.HttpInvalidResponse,
                $"Failed to deserialize response: {ex.Message}");
        }
    }

    private async Task EnsureSuccessWithMetricsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // Extract and process usage metrics from headers
        var usageMetrics = ExtractUsageMetrics(response);
        if (usageMetrics != null)
        {
            ProcessUsageMetrics(usageMetrics);
        }

        if (response.IsSuccessStatusCode) return;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;

        var (errorCode, category) = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => (ErrorCode.HttpBadRequest, "Client Error"),
            HttpStatusCode.Unauthorized => (ErrorCode.HttpUnauthorized, "Authentication Error"),
            HttpStatusCode.Forbidden => (ErrorCode.HttpForbidden, "Authorization Error"),
            HttpStatusCode.NotFound => (ErrorCode.HttpNotFound, "Not Found"),
            HttpStatusCode.TooManyRequests => (ErrorCode.HttpRateLimited, "Rate Limited"),
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout => (ErrorCode.HttpServerError, "Server Error"),
            _ when statusCode >= 400 && statusCode < 500 => (ErrorCode.HttpBadRequest, "Client Error"),
            _ => (ErrorCode.HttpServerError, "Server Error")
        };

        throw FlagKitException.NetworkError(
            errorCode,
            $"{category}: {statusCode} - {content}");
    }

    /// <summary>
    /// Extract usage metrics from response headers.
    /// </summary>
    /// <param name="response">The HTTP response.</param>
    /// <returns>UsageMetrics if any usage headers present, null otherwise.</returns>
    private UsageMetrics? ExtractUsageMetrics(HttpResponseMessage response)
    {
        var headers = response.Headers;

        string? apiUsage = null;
        string? evalUsage = null;
        string? rateLimitWarning = null;
        string? subscriptionStatus = null;

        if (headers.TryGetValues("X-API-Usage-Percent", out var apiUsageValues))
            apiUsage = apiUsageValues.FirstOrDefault();
        if (headers.TryGetValues("X-Evaluation-Usage-Percent", out var evalUsageValues))
            evalUsage = evalUsageValues.FirstOrDefault();
        if (headers.TryGetValues("X-Rate-Limit-Warning", out var rateLimitValues))
            rateLimitWarning = rateLimitValues.FirstOrDefault();
        if (headers.TryGetValues("X-Subscription-Status", out var statusValues))
            subscriptionStatus = statusValues.FirstOrDefault();

        // Return null if no usage headers present
        if (string.IsNullOrEmpty(apiUsage) &&
            string.IsNullOrEmpty(evalUsage) &&
            string.IsNullOrEmpty(rateLimitWarning) &&
            string.IsNullOrEmpty(subscriptionStatus))
        {
            return null;
        }

        double? apiUsagePercent = null;
        double? evalUsagePercent = null;

        if (!string.IsNullOrEmpty(apiUsage) && double.TryParse(apiUsage, out var apiParsed))
        {
            apiUsagePercent = apiParsed;
        }

        if (!string.IsNullOrEmpty(evalUsage) && double.TryParse(evalUsage, out var evalParsed))
        {
            evalUsagePercent = evalParsed;
        }

        // Validate subscription status
        string? validatedStatus = null;
        if (!string.IsNullOrEmpty(subscriptionStatus) &&
            ValidSubscriptionStatuses.Contains(subscriptionStatus.ToLowerInvariant()))
        {
            validatedStatus = subscriptionStatus.ToLowerInvariant();
        }

        return new UsageMetrics
        {
            ApiUsagePercent = apiUsagePercent,
            EvaluationUsagePercent = evalUsagePercent,
            RateLimitWarning = string.Equals(rateLimitWarning, "true", StringComparison.OrdinalIgnoreCase),
            SubscriptionStatus = validatedStatus
        };
    }

    /// <summary>
    /// Process usage metrics by logging warnings and invoking callbacks.
    /// </summary>
    /// <param name="metrics">The usage metrics to process.</param>
    private void ProcessUsageMetrics(UsageMetrics metrics)
    {
        // Log warnings for high usage
        if (metrics.ApiUsagePercent.HasValue && metrics.ApiUsagePercent >= 80)
        {
            Console.WriteLine($"[FlagKit] WARNING: API usage at {metrics.ApiUsagePercent}%");
        }

        if (metrics.EvaluationUsagePercent.HasValue && metrics.EvaluationUsagePercent >= 80)
        {
            Console.WriteLine($"[FlagKit] WARNING: Evaluation usage at {metrics.EvaluationUsagePercent}%");
        }

        if (metrics.SubscriptionStatus == "suspended")
        {
            Console.WriteLine("[FlagKit] ERROR: Subscription suspended - service degraded");
        }

        // Invoke callback if configured
        _options.OnUsageUpdate?.Invoke(metrics);
    }

    private static string GetVersion()
    {
        return typeof(FlagKitHttpClient).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}
